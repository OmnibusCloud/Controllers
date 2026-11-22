"""PF-0 spike: one environment replays everywhere. `leanchecker` over a slice of Mathlib.

    replay.py --lean-root <toolchain root> --work <dir> --out <results dir>
              [--random N] [--bignum N] [--seed S] [--together]

The compiled files come from Mathlib's cache (built on Mathlib's own machines); every platform
replays the same modules with its own `leanchecker`: each module's declarations go through the
kernel again on top of the module's imports. The slice is chosen the same way on every
platform: N modules by a seeded choice over the sorted module names, and N modules whose
sources hold the longest number literals (the kernel's big-number arithmetic is where a
platform could differ). Only modules that are not a prefix of another module's name are
taken, because `leanchecker` treats its argument as a prefix. Each module is replayed by its
own process (time, peak memory); with --together all of them once more in one process.
"""
from __future__ import annotations

import argparse
import json
import os
import random
import re
import sys

from pf0lib import EXE, lean_environment, run_measured, write_json

# A literal of 20 digits or more does not fit a machine word (2^64 has 20 digits).
DIGITS = re.compile(r"(?<![\w.])\d{20,}(?![\w.])")


def modules_of(source: str) -> dict:
    """Module name -> source path, for Mathlib's own modules."""
    found = {}
    root = os.path.join(source, "Mathlib")
    for folder, _dirs, files in os.walk(root):
        for name in files:
            if name.endswith(".lean"):
                path = os.path.join(folder, name)
                relative = os.path.relpath(path, source)[:-len(".lean")]
                found[relative.replace(os.sep, ".")] = path
    return found


def choose(source: str, count_random: int, count_bignum: int, seed: int) -> list:
    found = modules_of(source)
    names = sorted(found)
    prefixes = set()
    for name in names:
        parts = name.split(".")
        for cut in range(1, len(parts)):
            prefixes.add(".".join(parts[:cut]))
    leaves = [name for name in names if name not in prefixes]
    ranked = []
    for name in leaves:
        with open(found[name], encoding="utf-8", errors="replace") as handle:
            runs = DIGITS.findall(handle.read())
        if runs:
            ranked.append((-max(len(run) for run in runs), -len(runs), name))
    ranked.sort()
    bignum = [(name, "bignum", -longest) for longest, _count, name in ranked[:count_bignum]]
    taken = {name for name, _kind, _digits in bignum}
    rest = [name for name in leaves if name not in taken]
    # The modules with long literals are few: the seeded choice fills the slice up.
    count_random += count_bignum - len(bignum)
    chosen = random.Random(seed).sample(rest, min(count_random, len(rest)))
    return bignum + [(name, "random", 0) for name in sorted(chosen)]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lean-root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--random", type=int, default=25)
    parser.add_argument("--bignum", type=int, default=25)
    parser.add_argument("--seed", type=int, default=20261122)
    parser.add_argument("--together", action="store_true")
    options = parser.parse_args()
    work = os.path.abspath(options.work)
    bin_dir = os.path.join(os.path.abspath(options.lean_root), "bin")
    checker = os.path.join(bin_dir, "leanchecker" + EXE)
    source = os.path.join(work, "mathlib4")
    with open(os.path.join(work, "leanpath.json"), encoding="utf-8") as handle:
        lean_path = json.load(handle)
    env = lean_environment(lean_path, bin_dir=bin_dir)
    chosen = choose(source, options.random, options.bignum, options.seed)
    print(f"{len(chosen)} modules: {sum(1 for _n, kind, _d in chosen if kind == 'bignum')} by number literals "
          f"(longest {max([d for _n, _k, d in chosen] or [0])} digits), the rest by seed {options.seed}")

    rows = []
    failed = 0
    for name, kind, digits in chosen:
        out = run_measured([checker, name], env=env, cwd=source, timeout=3600)
        row = {"module": name, "kind": kind, "digits": digits, "exit": out["exit"], "wall_s": out["wall_s"],
               "user_s": out["user_s"], "sys_s": out["sys_s"], "peak_rss": out["peak_rss"],
               "peak_private": out.get("peak_private")}
        if out["exit"] != 0:
            failed += 1
            row["output"] = (out["stdout"] + out["stderr"])[-600:]
            print(f"FAILED {name}: {row['output']}")
        rows.append(row)
        print(f"{name}: exit {out['exit']}, {out['wall_s']} s, peak {out['peak_rss'] >> 20} MiB", flush=True)
    record = {"modules": rows, "replayed": len(rows) - failed, "failed": failed,
              "wall_s": round(sum(row["wall_s"] for row in rows), 2),
              "cpu_s": round(sum(row["user_s"] + row["sys_s"] for row in rows), 2),
              "peak_rss_max": max(row["peak_rss"] for row in rows) if rows else 0}
    if options.together and rows:
        # `leanchecker` replays several modules in one process, each as a task: tried with a
        # few modules and with the whole slice.
        record["together"] = []
        for count in (2, 5, len(chosen)):
            names = [name for name, _kind, _digits in chosen[:count]]
            out = run_measured([checker] + names, env=env, cwd=source, timeout=3600)
            entry = {"modules": len(names), "exit": out["exit"], "killed": out["killed"], "wall_s": out["wall_s"],
                     "user_s": out["user_s"], "sys_s": out["sys_s"], "peak_rss": out["peak_rss"],
                     "peak_private": out.get("peak_private"),
                     "output": (out["stdout"] + out["stderr"])[-400:] if out["exit"] != 0 else ""}
            record["together"].append(entry)
            print(f"together: {entry}", flush=True)
    write_json(os.path.join(options.out, "replay.json"), record)
    print(f"replayed {record['replayed']} of {len(rows)} modules, {record['wall_s']} s in all, "
          f"largest peak {record['peak_rss_max'] >> 20} MiB")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
