"""PF-0 spike: the Linux sandbox, by plain checks only.

    sandbox.py --lean-root <toolchain root> --work <dir> --out <results dir> --landrun <landrun>
               [--items items.jsonl] [--limit N]

Records the kernel, the Landlock ABI it offers and the state of user namespaces, then runs
under `landrun`, with the policy a check would run under (toolchain and environment read-only,
one scratch folder writable, no network rule):

  * a write inside the scratch folder (must succeed) and one outside it (must be refused);
  * a TCP connection and a UDP datagram to a public address (each is tried without the sandbox
    first: the refusal counts only where the plain attempt works);
  * the Mathlib smoke items (or the given items) through the fresh-process path, with and
    without the sandbox: the same verdicts, and what the sandbox costs.

It also says whether `bwrap`, the sandbox `lake comparator` asks for, can start here.
"""
from __future__ import annotations

import argparse
import ctypes
import json
import os
import platform
import subprocess
import sys

from pf0lib import EXE, read_jsonl, run_measured, write_json

HERE = os.path.dirname(os.path.abspath(__file__))
SPIKE = os.path.dirname(HERE)

TCP = "import socket; socket.create_connection(('1.1.1.1', 443), 8).close(); print('connected')"
UDP = ("import socket; s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); "
       "print('sent', s.sendto(b'x', ('1.1.1.1', 9)))")


def landlock_abi() -> int:
    """The Landlock ABI version of the running kernel (landlock_create_ruleset with the
    version flag), or a negative number when Landlock is off."""
    try:
        libc = ctypes.CDLL(None, use_errno=True)
        libc.syscall.restype = ctypes.c_long
        return int(libc.syscall(ctypes.c_long(444), None, ctypes.c_size_t(0), ctypes.c_uint(1)))
    except (OSError, AttributeError):
        return -1


def brief(out: dict) -> dict:
    text = (out["stdout"] + " " + out["stderr"]).strip()
    return {"exit": out["exit"], "says": " ".join(text.split())[-300:]}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lean-root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--landrun", required=True)
    parser.add_argument("--items", default=os.path.join(SPIKE, "golden", "smoke-mathlib.jsonl"))
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--jobs", type=int, default=2)
    options = parser.parse_args()
    work = os.path.abspath(options.work)
    root = os.path.abspath(options.lean_root)
    scratch = os.path.join(work, "sandbox-scratch")
    os.makedirs(scratch, exist_ok=True)
    outside = os.path.join(work, "sandbox-outside.txt")
    python = "/usr/bin/python3"
    record = {"kernel": platform.release(), "landlock_abi": landlock_abi()}

    policy = [options.landrun, "--rox", f"{root},/usr", "--ro",
              f"{os.path.join(work, 'mathlib4')},{os.path.join(work, 'prelude')},/etc,/proc,/sys,/dev",
              "--rw", scratch, "--env", "PATH", "--env", "LEAN_PATH", "--env", "LEAN_ABORT_ON_PANIC", "--"]
    record["policy"] = " ".join(policy).replace(work, "<work>").replace(root, "<toolchain>")

    checks = {}
    checks["write_inside"] = brief(run_measured(policy + ["/usr/bin/sh", "-c", f"echo ok > {scratch}/inside.txt && cat {scratch}/inside.txt"], timeout=60))
    checks["write_outside"] = brief(run_measured(policy + ["/usr/bin/sh", "-c", f"echo no > {outside}"], timeout=60))
    checks["write_outside"]["file_exists"] = os.path.exists(outside)
    checks["read_outside"] = brief(run_measured(policy + ["/usr/bin/ls", os.path.expanduser("~")], timeout=60))
    for name, code in (("tcp", TCP), ("udp", UDP)):
        checks[name + "_plain"] = brief(run_measured([python, "-c", code], timeout=60))
        checks[name + "_sandboxed"] = brief(run_measured(policy + [python, "-c", code], timeout=60))
    record["checks"] = checks
    record["holds"] = {
        "write_inside_allowed": checks["write_inside"]["exit"] == 0,
        "write_outside_refused": checks["write_outside"]["exit"] != 0 and not checks["write_outside"]["file_exists"],
        "tcp_refused": checks["tcp_plain"]["exit"] == 0 and checks["tcp_sandboxed"]["exit"] != 0,
        "udp_refused": checks["udp_plain"]["exit"] == 0 and checks["udp_sandboxed"]["exit"] != 0,
    }
    for key, value in checks.items():
        print(f"{key}: {value}")
    print(f"holds: {record['holds']}", flush=True)

    # The same items with and without the sandbox.
    lean = os.path.join(root, "bin", "lean" + EXE)
    common = [sys.executable, os.path.join(HERE, "check.py"), "--lean", lean,
              "--lean-path", os.path.join(work, "prelude"), "--lean-path-file", os.path.join(work, "leanpath.json"),
              "--import", "Mathlib", "--items", options.items, "--jobs", str(options.jobs), "--sample", "0"]
    if options.limit:
        common += ["--limit", str(options.limit)]
    runs = {}
    for name, extra in (("plain", []), ("landrun", ["--wrap", " ".join(policy)])):
        rows_path = os.path.join(options.out, f"sandbox-{name}.jsonl")
        out = run_measured(common + ["--out", rows_path, "--work", scratch] + extra, timeout=7200)
        print(out["stdout"][-600:], out["stderr"][-600:], flush=True)
        rows = read_jsonl(rows_path) if os.path.isfile(rows_path) else []
        runs[name] = {"wall_s": out["wall_s"], "items": len(rows),
                      "rows": {row["id"]: (row["verdict"] or row["status"], row.get("heartbeats")) for row in rows}}
    same = sum(1 for key, value in runs["plain"]["rows"].items() if runs["landrun"]["rows"].get(key) == value)
    record["items"] = {"count": runs["plain"]["items"], "same_verdict_and_heartbeats": same,
                       "plain_wall_s": runs["plain"]["wall_s"], "landrun_wall_s": runs["landrun"]["wall_s"],
                       "differing": {key: [value, runs["landrun"]["rows"].get(key)]
                                     for key, value in runs["plain"]["rows"].items()
                                     if runs["landrun"]["rows"].get(key) != value}}
    print(json.dumps(record["items"]))

    # bwrap: what `lake comparator` and `lake check` sandbox with.
    bwrap = {}
    which = subprocess.run(["sh", "-c", "command -v bwrap"], capture_output=True, text=True).stdout.strip()
    bwrap["installed"] = which
    if which:
        bwrap["version"] = brief(run_measured([which, "--version"], timeout=30))["says"]
        bwrap["starts"] = brief(run_measured([which, "--ro-bind", "/", "/", "--unshare-all", "/usr/bin/true"], timeout=30))
    bwrap["userns_restricted"] = subprocess.run(
        ["sh", "-c", "sysctl -n kernel.apparmor_restrict_unprivileged_userns 2>/dev/null"],
        capture_output=True, text=True).stdout.strip()
    record["bwrap"] = bwrap
    print(f"bwrap: {bwrap}")
    write_json(os.path.join(options.out, "sandbox.json"), record)
    return 0


if __name__ == "__main__":
    sys.exit(main())
