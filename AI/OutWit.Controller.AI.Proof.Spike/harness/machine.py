"""PF-0 spike: what the runner is, and where the gate's work folder goes.

    machine.py <record.json>

Records the operating system, kernel, CPU, memory and free disk; on Linux also the Landlock
state of the kernel. Chooses the work folder on the volume with the most free space and, under
GitHub Actions, exports it as PF0_WORK. Where PF0_WORK is set already (a machine that is not a
hosted runner), that folder is the work folder and nothing is created anywhere else.
"""
from __future__ import annotations

import os
import platform
import shutil
import subprocess
import sys

from pf0lib import IS_LINUX, IS_MACOS, IS_WINDOWS, write_json


def total_memory() -> int:
    try:
        if IS_LINUX:
            with open("/proc/meminfo", encoding="ascii") as handle:
                for line in handle:
                    if line.startswith("MemTotal:"):
                        return int(line.split()[1]) * 1024
        if IS_MACOS:
            return int(subprocess.run(["sysctl", "-n", "hw.memsize"], capture_output=True, text=True).stdout)
        if IS_WINDOWS:
            import ctypes

            class Status(ctypes.Structure):
                _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                            ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                            ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                            ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                            ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]

            status = Status()
            status.dwLength = ctypes.sizeof(status)
            ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status))
            return int(status.ullTotalPhys)
    except (OSError, ValueError):
        pass
    return 0


def text_of(cmd: list) -> str:
    try:
        return subprocess.run(cmd, capture_output=True, text=True, timeout=30).stdout.strip()
    except (OSError, subprocess.SubprocessError):
        return ""


def main() -> int:
    record = {
        "system": platform.system(), "release": platform.release(), "version": platform.version(),
        "machine": platform.machine(), "python": sys.version.split()[0],
        "cpus": os.cpu_count(), "memory_bytes": total_memory(),
        "runner_os": os.environ.get("RUNNER_OS"), "runner_arch": os.environ.get("RUNNER_ARCH"),
        "image": os.environ.get("ImageOS"), "image_version": os.environ.get("ImageVersion"),
    }
    if IS_LINUX:
        record["cpu_model"] = text_of(["sh", "-c", "grep -m1 'model name' /proc/cpuinfo | cut -d: -f2"])
        record["lsm"] = text_of(["cat", "/sys/kernel/security/lsm"])
        record["userns_restricted"] = text_of(["sh", "-c", "sysctl -n kernel.apparmor_restrict_unprivileged_userns 2>/dev/null"])
        record["bwrap"] = text_of(["sh", "-c", "command -v bwrap && bwrap --version"])
    elif IS_MACOS:
        record["cpu_model"] = text_of(["sysctl", "-n", "machdep.cpu.brand_string"])
    else:
        record["cpu_model"] = os.environ.get("PROCESSOR_IDENTIFIER", "")
        record["processor_architecture"] = os.environ.get("PROCESSOR_ARCHITECTURE", "")

    candidates = [os.environ.get("RUNNER_TEMP") or os.getcwd(), os.path.expanduser("~")]
    if IS_WINDOWS:
        candidates.append("C:\\")
    if IS_LINUX and os.path.isdir("/mnt") and os.access("/mnt", os.W_OK):
        candidates.append("/mnt")
    disks = {}
    best, best_free = None, -1
    for folder in candidates:
        try:
            free = shutil.disk_usage(folder).free
        except OSError:
            continue
        disks[folder.replace("\\", "/")] = free
        if free > best_free:
            best, best_free = folder, free
    record["disk_free"] = disks
    work = (os.environ.get("PF0_WORK") or os.path.join(best, "pf0")).replace("\\", "/")
    os.makedirs(work, exist_ok=True)
    record["work"] = work
    write_json(sys.argv[1], record)
    for key in sorted(record):
        print(f"{key}: {record[key]}")
    github_env = os.environ.get("GITHUB_ENV")
    if github_env:
        with open(github_env, "a", encoding="utf-8") as handle:
            handle.write(f"PF0_WORK={work}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
