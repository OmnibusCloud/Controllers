"""PF-2 spike: one command to its end with the memory of its WHOLE process tree measured.

PF-1's runner (pf1/common.py `capped`) reports the peak resident set of one process (wait4 gives
the largest of the process and its waited children, not their sum). A full stage is comparator
and the kernel it starts, both holding a parse of the export, so PF-2 samples the tree: every
0.5 s the resident bytes of the process and all its descendants are summed (psutil), and on Linux
inside a systemd scope the scope's anonymous memory (cgroup memory.stat `anon`: what the tree
alone holds, no shared page counted twice, no file cache). Caps as PF-1: on Linux a systemd scope
(`MemoryMax`, `RuntimeMaxSec`) that ends the tree; elsewhere a kill of the tree when the sampled
sum passes the cap (or a wall cap).
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time

try:
    import psutil
except ImportError:  # only `run_tree` needs it
    psutil = None

IS_WINDOWS = sys.platform == "win32"
IS_LINUX = sys.platform.startswith("linux")
_UNITS = [0]


def _tree(root: psutil.Process) -> list:
    try:
        return [root] + root.children(recursive=True)
    except psutil.Error:
        return [root]


def _sum_rss(processes: list) -> tuple[int, int, int]:
    rss = private = count = 0
    for proc in processes:
        try:
            info = proc.memory_info()
        except psutil.Error:
            continue
        rss += info.rss
        private += getattr(info, "private", 0) or getattr(info, "uss", 0) or 0
        count += 1
    return rss, private, count


def _available_commit() -> int | None:
    """Windows: the commit the whole machine has left (a run that exhausts it breaks every
    process of the machine, the fleet client's included: PF-2 saw it)."""
    if not IS_WINDOWS:
        return None
    import ctypes

    class Status(ctypes.Structure):
        _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                    ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                    ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                    ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                    ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]

    status = Status()
    status.dwLength = ctypes.sizeof(status)
    if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
        return None
    return int(status.ullAvailPageFile)


def _cgroup_anon(unit: str) -> int | None:
    for base in ("/sys/fs/cgroup/system.slice", "/sys/fs/cgroup"):
        path = os.path.join(base, unit + ".scope", "memory.stat")
        try:
            with open(path, encoding="ascii") as handle:
                for line in handle:
                    if line.startswith("anon "):
                        return int(line.split()[1])
        except OSError:
            continue
    return None


def run_tree(cmd: list, env: dict | None = None, cwd: str | None = None, timeout: float = 3600,
             memory_gib: float | None = None, guard_gib: float | None = None, stdout_path: str | None = None,
             keep: int = 1 << 16, stack_unlimited: bool = False) -> dict:
    """Runs `cmd` to its end. `memory_gib` is the tier's cap (a scope on Linux when PF1_CGROUP=1, a
    sampled kill elsewhere); `guard_gib` a sampled kill that only protects the machine (the
    workstation's runs "without a cap"). Returns wall and CPU seconds, the tree's peaks, the exit
    code or signal, and how the run ended."""
    cap_bytes = int(memory_gib * (1 << 30)) if memory_gib else None
    guard_bytes = int(guard_gib * (1 << 30)) if guard_gib else None
    unit = None
    run_cmd = list(cmd)
    if stack_unlimited and not IS_WINDOWS:
        run_cmd = ["bash", "-c", 'ulimit -s unlimited 2>/dev/null || ulimit -s hard 2>/dev/null; exec "$@"', "pf2"] + run_cmd
    if cap_bytes and IS_LINUX and shutil.which("systemd-run") and os.environ.get("PF1_CGROUP") == "1":
        _UNITS[0] += 1
        unit = f"pf2-cap-{os.getpid()}-{_UNITS[0]}"
        wrapper = ["sudo", "-n", "systemd-run", "--quiet", "--scope", f"--unit={unit}", "-p", f"MemoryMax={cap_bytes}",
                   "-p", "MemorySwapMax=0", "-p", f"RuntimeMaxSec={int(timeout)}",
                   f"--uid={os.getuid()}", f"--gid={os.getgid()}", "--setenv=HOME=" + os.environ.get("HOME", "/tmp")]
        for key in ("PATH", "LEAN_PATH", "LEAN_ABORT_ON_PANIC", "LD_LIBRARY_PATH"):
            if env and key in env:
                wrapper.append(f"--setenv={key}={env[key]}")
        run_cmd = wrapper + ["--"] + run_cmd
        sampled_cap = None
    else:
        sampled_cap = cap_bytes
    out_handle = open(stdout_path, "wb") if stdout_path else tempfile.TemporaryFile()
    err_handle = tempfile.TemporaryFile()
    started = time.monotonic()
    wall_clock = time.time()
    row = {"started_at": round(wall_clock, 1), "cap": "scope" if unit else ("sampled" if sampled_cap else None),
           "cap_gib": memory_gib, "guard_gib": guard_gib, "cap_s": timeout}
    try:
        proc = subprocess.Popen(run_cmd, env=env, cwd=cwd, stdin=subprocess.DEVNULL, stdout=out_handle, stderr=err_handle,
                                creationflags=(0x00004000 if IS_WINDOWS and os.environ.get("PF1_BELOW_NORMAL") == "1" else 0))
    except OSError as error:
        out_handle.close()
        err_handle.close()
        row.update({"exit": None, "error": str(error), "wall_s": 0.0, "ending": "could_not_start"})
        return row
    root = psutil.Process(proc.pid)
    peaks = {"tree_rss": 0, "tree_private": 0, "tree_processes": 0, "scope_anon": 0}
    killed = [None]
    cpu = {"user": 0.0, "system": 0.0}
    seen_cpu = {}
    stop = threading.Event()

    def sample() -> None:
        while not stop.is_set():
            try:
                sample_once()
            except Exception as error:  # the guard must outlive a failed sample
                row.setdefault("sample_errors", []).append(str(error)[:200])
            stop.wait(0.5)

    def sample_once() -> None:
        if True:
            processes = _tree(root)
            rss, private, count = _sum_rss(processes)
            for p in processes:
                try:
                    times = p.cpu_times()
                    seen_cpu[p.pid] = (times.user, times.system)
                except psutil.Error:
                    pass
            peaks["tree_rss"] = max(peaks["tree_rss"], rss)
            peaks["tree_private"] = max(peaks["tree_private"], private)
            peaks["tree_processes"] = max(peaks["tree_processes"], count)
            if unit:
                anon = _cgroup_anon(unit)
                if anon is not None:
                    peaks["scope_anon"] = max(peaks["scope_anon"], anon)
            now = time.monotonic()
            if killed[0] is None:
                if not unit and now - started > timeout:
                    killed[0] = "wall_cap"
                elif sampled_cap and rss > sampled_cap:
                    killed[0] = "memory_cap"
                elif guard_bytes and (rss > guard_bytes or (IS_WINDOWS and private > guard_bytes)):
                    killed[0] = "guard"
                elif IS_WINDOWS and guard_bytes and (_available_commit() or 1 << 62) < (8 << 30):
                    killed[0] = "guard_commit"
                if killed[0] is not None:
                    for p in reversed(processes):
                        try:
                            p.kill()
                        except psutil.Error:
                            pass

    sampler = threading.Thread(target=sample, daemon=True)
    sampler.start()
    usage = None
    if IS_WINDOWS:
        exit_code = proc.wait()
    else:
        _, status, usage = os.wait4(proc.pid, 0)
        exit_code = os.waitstatus_to_exitcode(status)
        proc.returncode = exit_code
    wall = time.monotonic() - started
    stop.set()
    sampler.join(timeout=5)
    if unit:
        subprocess.run(["sudo", "-n", "systemctl", "kill", "--signal=KILL", unit + ".scope"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if usage is not None:
        cpu = {"user": round(usage.ru_utime, 1), "system": round(usage.ru_stime, 1),
               "max_rss_one_process": int(usage.ru_maxrss * (1 if sys.platform == "darwin" else 1024))}
    else:
        cpu = {"user": round(sum(u for u, _ in seen_cpu.values()), 1),
               "system": round(sum(s for _, s in seen_cpu.values()), 1)}
    stdout_text = ""
    if not stdout_path:
        out_handle.seek(0)
        stdout_text = out_handle.read(keep).decode("utf-8", "replace")
    err_handle.seek(0)
    text = (stdout_text + "\n" + err_handle.read(keep).decode("utf-8", "replace")).strip()
    out_handle.close()
    err_handle.close()
    row.update({"exit": exit_code, "wall_s": round(wall, 1), "cpu": cpu, "killed": killed[0], **peaks})
    # How the run ended, in the certificate's words (spec section 6.5): only exit 0 is "accepted".
    if killed[0] in ("memory_cap", "guard", "guard_commit"):
        ending = "memory_cap"
    elif killed[0] == "wall_cap":
        ending = "wall_cap"
    elif unit and exit_code in (-9, 137) and peaks["scope_anon"] >= 0.85 * cap_bytes:
        ending = "memory_cap"
    elif unit and exit_code in (-9, 137, -15, 143) and wall >= timeout - 2:
        ending = "wall_cap"
    elif exit_code == 0:
        ending = "accepted"
    elif exit_code is not None and (exit_code < 0 or exit_code >= 128 or (IS_WINDOWS and exit_code > 0xC0000000)):
        ending = "crashed"
    else:
        ending = "rejected"
    row["ending"] = ending
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    row["says"] = " / ".join(lines[-8:])[-900:]
    row["first"] = lines[0][:300] if lines else ""
    row["stdout_tail"] = stdout_text[-4000:]
    return row


def gib(value: int | None) -> float | None:
    return round(value / (1 << 30), 2) if value else None
