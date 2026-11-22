"""PF-0 spike: what every harness script shares.

Pins, hashing, a process runner that reports wall time, CPU time and peak resident memory on
Linux, macOS and Windows without third-party packages, and the assembly of one check item (a
trusted challenge and an untrusted solution) into the text Lean elaborates.
"""
from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import tempfile
import threading
import time

IS_WINDOWS = os.name == "nt"
IS_MACOS = sys.platform == "darwin"
IS_LINUX = sys.platform.startswith("linux")
EXE = ".exe" if IS_WINDOWS else ""

STANDARD_AXIOMS = ("propext", "Classical.choice", "Quot.sound")
SORRY_AXIOM = "sorryAx"
# The words Lean's own budget error carries (Core.checkMaxHeartbeats).
BUDGET_MARKERS = ("maximum number of heartbeats", "(deterministic) timeout")


def load_pins(path: str) -> dict:
    pins = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            pins[key.strip()] = value.strip()
    return pins


def rid_key(rid: str) -> str:
    return rid.upper().replace("-", "_")


def sha256_file(path: str, chunk: int = 1 << 20) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            block = handle.read(chunk)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def tree_bytes(root: str) -> tuple[int, int]:
    """Bytes and file count under a folder (symbolic links counted as links, not targets)."""
    total = 0
    count = 0
    for folder, _dirs, files in os.walk(root):
        for name in files:
            path = os.path.join(folder, name)
            try:
                total += os.lstat(path).st_size
                count += 1
            except OSError:
                pass
    return total, count


def write_json(path: str, value) -> None:
    folder = os.path.dirname(os.path.abspath(path))
    os.makedirs(folder, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(value, handle, indent=1, sort_keys=True)
        handle.write("\n")


def read_jsonl(path: str) -> list:
    rows = []
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


class JsonlWriter:
    """Appends rows from several threads, one line each, flushed as written."""

    def __init__(self, path: str):
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        self._handle = open(path, "w", encoding="utf-8", newline="\n")
        self._lock = threading.Lock()

    def write(self, row: dict) -> None:
        line = json.dumps(row, sort_keys=True, ensure_ascii=True)
        with self._lock:
            self._handle.write(line + "\n")
            self._handle.flush()

    def close(self) -> None:
        self._handle.close()


# --------------------------------------------------------------------------------------------
# Processes: wall, CPU, peak resident memory
# --------------------------------------------------------------------------------------------

if IS_WINDOWS:
    import ctypes
    import ctypes.wintypes as wintypes

    class _ProcessMemoryCounters(ctypes.Structure):
        _fields_ = [
            ("cb", wintypes.DWORD),
            ("PageFaultCount", wintypes.DWORD),
            ("PeakWorkingSetSize", ctypes.c_size_t),
            ("WorkingSetSize", ctypes.c_size_t),
            ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
            ("QuotaPagedPoolUsage", ctypes.c_size_t),
            ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
            ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
            ("PagefileUsage", ctypes.c_size_t),
            ("PeakPagefileUsage", ctypes.c_size_t),
            ("PrivateUsage", ctypes.c_size_t),
        ]

    _kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    _kernel32.K32GetProcessMemoryInfo.argtypes = [
        wintypes.HANDLE, ctypes.POINTER(_ProcessMemoryCounters), wintypes.DWORD]
    _kernel32.K32GetProcessMemoryInfo.restype = wintypes.BOOL
    _kernel32.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
    _kernel32.GetProcessTimes.restype = wintypes.BOOL

    def _windows_memory(handle) -> dict:
        counters = _ProcessMemoryCounters()
        counters.cb = ctypes.sizeof(counters)
        if not _kernel32.K32GetProcessMemoryInfo(int(handle), ctypes.byref(counters), counters.cb):
            return {}
        return {
            "rss": counters.WorkingSetSize,
            "peak_rss": counters.PeakWorkingSetSize,
            "private": counters.PrivateUsage,
            "peak_private": counters.PeakPagefileUsage,
        }

    def _windows_cpu(handle) -> tuple[float, float]:
        created, exited, kernel, user = (wintypes.FILETIME() for _ in range(4))
        if not _kernel32.GetProcessTimes(int(handle), ctypes.byref(created), ctypes.byref(exited),
                                         ctypes.byref(kernel), ctypes.byref(user)):
            return 0.0, 0.0

        def seconds(value) -> float:
            return ((value.dwHighDateTime << 32) | value.dwLowDateTime) / 1e7

        return seconds(user), seconds(kernel)


if IS_MACOS:
    import ctypes

    class _RusageInfoV2(ctypes.Structure):
        _fields_ = [("ri_uuid", ctypes.c_uint8 * 16)] + [(name, ctypes.c_uint64) for name in (
            "ri_user_time", "ri_system_time", "ri_pkg_idle_wkups", "ri_interrupt_wkups", "ri_pageins",
            "ri_wired_size", "ri_resident_size", "ri_phys_footprint", "ri_proc_start_abstime",
            "ri_proc_exit_abstime", "ri_child_user_time", "ri_child_system_time",
            "ri_child_pkg_idle_wkups", "ri_child_interrupt_wkups", "ri_child_pageins",
            "ri_child_elapsed_abstime", "ri_diskio_bytesread", "ri_diskio_byteswritten")]

    try:
        _libproc = ctypes.CDLL("/usr/lib/libproc.dylib", use_errno=True)
    except OSError:
        _libproc = None

    def _macos_memory(pid: int) -> dict:
        """Resident size and the physical footprint (what the process alone answers for)."""
        if _libproc is None:
            return {}
        info = _RusageInfoV2()
        if _libproc.proc_pid_rusage(ctypes.c_int(pid), ctypes.c_int(2), ctypes.byref(info)) != 0:
            return {}
        return {"rss": int(info.ri_resident_size), "private": int(info.ri_phys_footprint)}


def live_memory(proc: subprocess.Popen) -> dict:
    """Resident and private bytes of a running process: the private part is what the process
    alone answers for (Linux: private pages; macOS: physical footprint; Windows: private
    commit), the rest of the resident set being pages of files other processes map too."""
    try:
        if IS_WINDOWS:
            return _windows_memory(proc._handle)
        if IS_MACOS:
            return _macos_memory(proc.pid)
        if IS_LINUX:
            out = {}
            with open(f"/proc/{proc.pid}/smaps_rollup", encoding="ascii") as handle:
                for line in handle:
                    parts = line.split()
                    if len(parts) >= 2 and parts[0].rstrip(":") in (
                            "Rss", "Pss", "Private_Clean", "Private_Dirty", "Shared_Clean", "Anonymous"):
                        out[parts[0].rstrip(":").lower()] = int(parts[1]) * 1024
            if "private_clean" in out and "private_dirty" in out:
                out["private"] = out["private_clean"] + out["private_dirty"]
            return out
        return {}
    except (OSError, ValueError):
        return {}


def system_memory() -> dict:
    """What the whole machine has left: the number that says how many processes fit."""
    try:
        if IS_LINUX:
            out = {}
            with open("/proc/meminfo", encoding="ascii") as handle:
                for line in handle:
                    parts = line.split()
                    if parts[0].rstrip(":") in ("MemTotal", "MemFree", "MemAvailable", "Cached", "AnonPages", "Mapped"):
                        out[parts[0].rstrip(":").lower()] = int(parts[1]) * 1024
            out["available"] = out.get("memavailable", 0)
            return out
        if IS_WINDOWS:
            class Status(ctypes.Structure):
                _fields_ = [("dwLength", wintypes.DWORD), ("dwMemoryLoad", wintypes.DWORD),
                            ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                            ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                            ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                            ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]

            status = Status()
            status.dwLength = ctypes.sizeof(status)
            _kernel32.GlobalMemoryStatusEx(ctypes.byref(status))
            return {"available": int(status.ullAvailPhys), "total": int(status.ullTotalPhys),
                    "commit_available": int(status.ullAvailPageFile), "commit_limit": int(status.ullTotalPageFile)}
        text = subprocess.run(["vm_stat"], capture_output=True, text=True, timeout=10).stdout
        page = 4096
        pages = {}
        for line in text.splitlines():
            if "page size of" in line:
                page = int(line.split("page size of")[1].split()[0])
            elif ":" in line:
                key, value = line.split(":", 1)
                try:
                    pages[key.strip()] = int(value.strip().rstrip("."))
                except ValueError:
                    pass
        free = sum(pages.get(key, 0) for key in ("Pages free", "Pages inactive", "Pages speculative", "Pages purgeable"))
        return {"available": free * page, "free": pages.get("Pages free", 0) * page,
                "file_backed": pages.get("File-backed pages", 0) * page,
                "anonymous": pages.get("Anonymous pages", 0) * page,
                "compressed": pages.get("Pages occupied by compressor", 0) * page}
    except (OSError, ValueError, subprocess.SubprocessError):
        return {}


def _live_rss(proc: subprocess.Popen) -> int:
    if IS_LINUX:
        try:
            with open(f"/proc/{proc.pid}/statm", encoding="ascii") as handle:
                return int(handle.read().split()[1]) * os.sysconf("SC_PAGE_SIZE")
        except (OSError, ValueError, IndexError):
            return 0
    return int(live_memory(proc).get("rss", 0))


def finish(proc: subprocess.Popen, started: float, timeout: float | None = None,
           memory_cap: int | None = None, poll: float = 0.02, sample: float = 0.25) -> dict:
    """Waits for a started process and reports what it cost.

    The exit is collected with wait4 on Linux and macOS (the kernel's own account of the child:
    peak resident set, user and system time) and from the process handle on Windows. A process
    still alive after `timeout` seconds, or seen above `memory_cap` resident bytes, is killed.
    On Linux and macOS the private part of the memory is sampled while the process runs (its
    peak is the largest sample); Windows keeps the peak itself.
    """
    killed = None
    usage = None
    status = None
    next_memory_check = 0.0
    next_sample = started + 0.05
    peak_private = 0
    while True:
        now = time.monotonic()
        if IS_WINDOWS:
            if proc.poll() is not None:
                break
        else:
            pid, status, usage = os.wait4(proc.pid, os.WNOHANG)
            if pid != 0:
                break
            if sample and now >= next_sample:
                next_sample = now + sample
                peak_private = max(peak_private, int(live_memory(proc).get("private", 0)))
        if killed is None:
            if timeout is not None and now - started > timeout:
                killed = "timeout"
            elif memory_cap is not None and now >= next_memory_check:
                next_memory_check = now + 0.1
                if _live_rss(proc) > memory_cap:
                    killed = "memory"
            if killed is not None:
                try:
                    proc.kill()
                except OSError:
                    pass
        time.sleep(poll)
    wall = time.monotonic() - started
    row = {"wall_s": round(wall, 4), "killed": killed}
    if IS_WINDOWS:
        memory = _windows_memory(proc._handle)
        user, kernel = _windows_cpu(proc._handle)
        row.update({
            "exit": proc.returncode,
            "user_s": round(user, 4),
            "sys_s": round(kernel, 4),
            "peak_rss": int(memory.get("peak_rss", 0)),
            "peak_private": int(memory.get("peak_private", 0)),
        })
    else:
        proc.returncode = os.waitstatus_to_exitcode(status)
        peak = usage.ru_maxrss if IS_MACOS else usage.ru_maxrss * 1024
        row.update({
            "exit": proc.returncode,
            "user_s": round(usage.ru_utime, 4),
            "sys_s": round(usage.ru_stime, 4),
            "peak_rss": int(peak),
            "peak_private": peak_private,
        })
    return row


def run_measured(cmd: list, env: dict | None = None, cwd: str | None = None,
                 timeout: float | None = None, memory_cap: int | None = None,
                 stdin_text: str | None = None, keep: int = 4 << 20, sample: float = 0.25,
                 stdout_path: str | None = None) -> dict:
    """Runs a command to its end; output goes to temporary files and comes back as text
    (standard output goes to `stdout_path` instead when one is given, and comes back empty)."""
    with (open(stdout_path, "wb") if stdout_path else tempfile.TemporaryFile()) as out, \
            tempfile.TemporaryFile() as err:
        stdin = subprocess.PIPE if stdin_text is not None else subprocess.DEVNULL
        started = time.monotonic()
        try:
            proc = subprocess.Popen(cmd, env=env, cwd=cwd, stdin=stdin, stdout=out, stderr=err)
        except OSError as error:
            return {"exit": None, "error": str(error), "wall_s": 0.0, "killed": None,
                    "stdout": "", "stderr": str(error), "peak_rss": 0, "user_s": 0.0, "sys_s": 0.0}
        if stdin_text is not None:
            try:
                proc.stdin.write(stdin_text.encode("utf-8"))
                proc.stdin.close()
            except OSError:
                pass
        row = finish(proc, started, timeout, memory_cap, sample=sample)
        row["stdout"] = ""
        for name, handle in (("stdout", None if stdout_path else out), ("stderr", err)):
            if handle is not None:
                handle.seek(0)
                row[name] = handle.read(keep).decode("utf-8", errors="replace")
    return row


# --------------------------------------------------------------------------------------------
# Lean
# --------------------------------------------------------------------------------------------

def lean_environment(lean_path: list, extra: dict | None = None, bin_dir: str | None = None) -> dict:
    env = dict(os.environ)
    env["LEAN_PATH"] = os.pathsep.join(lean_path)
    if bin_dir:
        # The toolchain's own tools find its root by asking the `lean` on PATH.
        env["PATH"] = bin_dir + os.pathsep + env.get("PATH", "")
    # A panic ends the process instead of going on with a broken state.
    env["LEAN_ABORT_ON_PANIC"] = "1"
    if extra:
        env.update(extra)
    return env


def parse_messages(stdout: str) -> list:
    """The messages of `lean --json`: one JSON object per line."""
    messages = []
    for line in stdout.splitlines():
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            value = json.loads(line)
        except ValueError:
            continue
        if isinstance(value, dict) and "severity" in value and "data" in value:
            messages.append(value)
    return messages


def assemble_body(item: dict, max_heartbeats: int) -> tuple[str, dict]:
    """The text of one item after the imports, and the line spans of its parts.

    The budget is set first (a trusted line: a candidate may not set it). The challenge is
    elaborated in a namespace of its own, so that its statement and the solution's can be
    compared in one environment; each command is wrapped in the measuring command of the
    prelude, which reports the heartbeats that command used on its thread. The same text goes
    to a fresh process (after the import lines) and to a pooled process (as one command).
    """
    lines = [f"set_option maxHeartbeats {max_heartbeats}"]
    for line in item.get("opens", []):
        lines.append(line)
    spans = {}

    def add(label: str, text: str) -> None:
        lines.append(f'#pf0_measure "{label}" in')
        first = len(lines) + 1
        lines.extend(text.rstrip().splitlines())
        spans[label] = (first, len(lines))

    lines.append("namespace PF0C")
    add("c", item["challenge"])
    lines.append("end PF0C")
    for index, text in enumerate(item.get("solution_pre", [])):
        add(f"p{index}", text)
    add("s", item["solution"])
    lines.append(f"#pf0_report {item['name']} PF0C.{item['name']}")
    return "\n".join(lines) + "\n", spans


def assemble(item: dict, imports: list, max_heartbeats: int) -> tuple[str, dict]:
    """The whole file of one item: the import lines, then the body."""
    body, spans = assemble_body(item, max_heartbeats)
    header = "".join(f"import {name}\n" for name in imports)
    shift = len(imports)
    return header + body, {label: (first + shift, last + shift) for label, (first, last) in spans.items()}


def classify(item: dict, messages: list, spans: dict, allow_native: bool = False) -> dict:
    """The verdict of one item from the messages of its elaboration."""
    marks = {}
    report = None
    errors = {"c": [], "s": [], "other": []}
    for message in messages:
        data = message.get("data", "")
        if message.get("severity") == "information" and data.startswith("PF0 "):
            try:
                value = json.loads(data[4:])
            except ValueError:
                continue
            if "mark" in value:
                marks[value["mark"]] = value
            elif "report" in value:
                report = value
            continue
        if message.get("severity") != "error":
            continue
        line = (message.get("pos") or {}).get("line", 0)
        where = "other"
        for label, (first, last) in spans.items():
            # The measuring command sits on the line above the text it wraps.
            if first - 1 <= line <= last:
                where = "s" if label.startswith(("s", "p")) else "c"
        errors[where].append(data)

    row = {
        "heartbeats": (marks.get("s") or {}).get("heartbeats"),
        "elab_ns": (marks.get("s") or {}).get("nanos"),
        "challenge_heartbeats": (marks.get("c") or {}).get("heartbeats"),
        "pre_heartbeats": sum(v.get("heartbeats", 0) for k, v in marks.items() if k.startswith("p")),
        "errors": len(errors["s"]) + len(errors["other"]),
        "challenge_errors": len(errors["c"]),
        "axioms": None, "same_type": None, "kind": None, "type_hash": None, "value_hash": None,
        "axiom_class": None, "first_error": None, "error_digest": None,
    }
    solution_errors = errors["s"] + errors["other"]
    if solution_errors:
        row["first_error"] = solution_errors[0][:240]
        row["error_digest"] = sha256_text("\n".join(solution_errors))[:16]
    if report is not None:
        row["axioms"] = sorted(report.get("axioms", []))
        row["same_type"] = report.get("same")
        row["kind"] = report.get("kind")
        row["type_hash"] = report.get("type_hash")
        row["value_hash"] = report.get("value_hash")
        row["challenge_kind"] = report.get("challenge_kind")

    if report is None:
        row["verdict"] = None
        row["status"] = "no_report"
    elif errors["c"] or report.get("challenge_kind") == "missing":
        row["verdict"] = None
        row["status"] = "challenge_error"
        row["first_error"] = (errors["c"] or ["the challenge declared nothing"])[0][:240]
    else:
        row["status"] = "ok"
        axioms = row["axioms"] or []
        extra = [a for a in axioms if a not in STANDARD_AXIOMS and a != SORRY_AXIOM]
        if extra:
            row["axiom_class"] = "native" if all(is_native_axiom(a) for a in extra) else "custom"
        elif SORRY_AXIOM in axioms:
            row["axiom_class"] = "incomplete"
        else:
            row["axiom_class"] = "standard"
        if solution_errors:
            budget = any(marker in text for text in solution_errors for marker in BUDGET_MARKERS)
            row["verdict"] = "budget" if budget else "rejected"
        elif row["kind"] == "missing":
            row["verdict"] = "rejected"
        elif SORRY_AXIOM in axioms:
            row["verdict"] = "incomplete"
        elif extra and not (allow_native and row["axiom_class"] == "native"):
            row["verdict"] = "axioms_refused"
        elif row["kind"] != "theorem" or not row["same_type"]:
            row["verdict"] = "statement_mismatch"
        else:
            row["verdict"] = "accepted"
    return row


def is_native_axiom(name: str) -> bool:
    """Axioms the compiler's evaluation stands behind (`native_decide`, `bv_decide`)."""
    lowered = name.lower()
    return (name in ("Lean.ofReduceBool", "Lean.reduceBool", "Lean.trustCompiler")
            or "native" in lowered or "ofreducebool" in lowered)
