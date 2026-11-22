"""PF-0 spike: what one checking process costs before it checks anything.

    startup.py --lean-root <toolchain root> --work <dir> --out <results dir> [--runs N]

Elaborates a file that only imports the profile's environment, several times, each in a fresh
process: wall time, CPU time and peak resident memory of the import alone; the first run is the
cold one (the compiled files are not yet in the page cache).
"""
from __future__ import annotations

import argparse
import json
import os
import sys

from pf0lib import EXE, SMALL_PROFILE, lean_environment, run_measured, system_memory, write_json


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lean-root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--runs", type=int, default=4)
    parser.add_argument("--timeout", type=float, default=900.0)
    options = parser.parse_args()
    lean = os.path.join(options.lean_root, "bin", "lean" + EXE)
    work = os.path.abspath(options.work)
    lean_path = json.load(open(os.path.join(work, "leanpath.json"), encoding="utf-8"))
    env = lean_environment(lean_path + [os.path.join(work, "prelude")])
    folder = os.path.join(work, "startup")
    os.makedirs(folder, exist_ok=True)
    record = {}
    small = "".join(f"import {name}\n" for name in SMALL_PROFILE)
    for name, text in (("core", "import Lean\n"), ("small", small), ("mathlib", "import Mathlib\n")):
        path = os.path.join(folder, f"{name}.lean")
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
        runs = []
        for index in range(options.runs):
            before = system_memory()
            out = run_measured([lean, "--json", "-DElab.async=false", f"{name}.lean"], env=env, cwd=folder,
                               timeout=options.timeout)
            row = {key: out.get(key) for key in ("wall_s", "user_s", "sys_s", "peak_rss", "peak_private", "exit", "killed")}
            row["available_before"] = before.get("available")
            runs.append(row)
            print(f"import {name} run {index}: {row}", flush=True)
            if out["exit"] != 0:
                print((out["stdout"] + out["stderr"])[-2000:])
                break
            if out["wall_s"] > 240 and index >= 1:
                # A machine this slow at it is not asked a third time.
                break
        record[name] = runs
        write_json(os.path.join(options.out, "startup.json"), record)
    return 0 if all(run["exit"] == 0 for runs in record.values() for run in runs) else 1


if __name__ == "__main__":
    sys.exit(main())
