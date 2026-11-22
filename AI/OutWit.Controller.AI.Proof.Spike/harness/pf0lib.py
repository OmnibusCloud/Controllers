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


def live_memory(proc: subprocess.Popen) -> dict:
    """Resident (and, where the system tells, private) bytes of a running process."""
    try:
        if IS_WINDOWS:
            return _windows_memory(proc._handle)
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
        text = subprocess.run(["ps", "-o", "rss=", "-p", str(proc.pid)], capture_output=True,
                              text=True, timeout=10).stdout.strip()
        return {"rss": int(text) * 1024} if text else {}
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
           memory_cap: int | None = None, poll: float = 0.02) -> dict:
    """Waits for a started process and reports what it cost.

    The exit is collected with wait4 on Linux and macOS (the kernel's own account of the child:
    peak resident set, user and system time) and from the process handle on Windows. A process
    still alive after `timeout` seconds, or seen above `memory_cap` resident bytes, is killed.
    """
    killed = None
    usage = None
    status = None
    next_memory_check = 0.0
    while True:
        now = time.monotonic()
        if IS_WINDOWS:
            if proc.poll() is not None:
                break
        else:
            pid, status, usage = os.wait4(proc.pid, os.WNOHANG)
            if pid != 0:
                break
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
        })
    return row


def run_measured(cmd: list, env: dict | None = None, cwd: str | None = None,
                 timeout: float | None = None, memory_cap: int | None = None,
                 stdin_text: str | None = None, keep: int = 4 << 20) -> dict:
    """Runs a command to its end; output goes to temporary files and comes back as text."""
    with tempfile.TemporaryFile() as out, tempfile.TemporaryFile() as err:
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
        row = finish(proc, started, timeout, memory_cap)
        for name, handle in (("stdout", out), ("stderr", err)):
            handle.seek(0)
            row[name] = handle.read(keep).decode("utf-8", errors="replace")
    return row


# --------------------------------------------------------------------------------------------
# Lean
# --------------------------------------------------------------------------------------------

def lean_environment(lean_path: list, extra: dict | None = None) -> dict:
    env = dict(os.environ)
    env["LEAN_PATH"] = os.pathsep.join(lean_path)
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


def assemble(item: dict, imports: list, opens: list | None = None) -> tuple[str, dict]:
    """The text of one item and the line spans of its parts.

    The challenge is elaborated first, in a namespace of its own, so that its statement and the
    solution's can be compared in one environment; each command is wrapped in the measuring
    command of the prelude, which reports the heartbeats that command used on its thread.
    """
    lines = [f"import {name}" for name in imports]
    for line in (opens or []):
        lines.append(line)
    for line in item.get("opens", []):
        lines.append(line)
    spans = {}

    def add(label: str, text: str, measured: bool = True) -> None:
        if measured:
            lines.append(f'#pf0_measure "{label}" in')
        first = len(lines) + 1
        lines.extend(text.rstrip("\n").split("\n"))
        spans[label] = (first, len(lines))

    lines.append("namespace PF0C")
    add("c", item["challenge"])
    lines.append("end PF0C")
    for index, text in enumerate(item.get("solution_pre", [])):
        add(f"p{index}", text)
    add("s", item["solution"])
    lines.append(f"#pf0_report {item['name']} PF0C.{item['name']}")
    return "\n".join(lines) + "\n", spans


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
