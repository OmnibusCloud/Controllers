"""PF-1 spike: the full stage on one machine (spec section 4.5, stage 2): a kit's whole closure
replayed by each kernel of the pinned toolchain, and `lake comparator` as the certificate runs it.

    replay.py --root <pinned lean root> --export <solution.ndjson> --challenge <challenge.ndjson>
              --theorems a,b --steps leanchecker,nanoda-4,con-ron,comparator,... --out <record.json>
              [--driver <exe>] [--slices 4] [--cap-min 120] [--sandbox]

Steps: `leanchecker`, `leanchecker-paranoid`, `nanoda-<threads>`, `con-ron`, `con-leche`,
`lean4lean` (each the command `lake comparator` starts for it); `comparator` (Lean's kernel only:
the certificate's command), `comparator-nanoda` (`enable_nanoda`), `comparator-paranoid`
(every bundled kernel), `comparator-bwrap` (inside bubblewrap, Linux); `driver-profile` (the
whole closure through the driver, one declaration at a time, timed) and `driver-slices` (the
closure cut into K slices by those times, each slice its own process, as K machines would run it).
Every step has a wall cap; the memory is measured, not capped.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time

from common import (IS_LINUX, capped, comparator_command, comparator_config, kernel_command, lean_env,
                    lower_priority, write_json)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    parser.add_argument("--export", required=True)
    parser.add_argument("--challenge", default="")
    parser.add_argument("--theorems", default="")
    parser.add_argument("--steps", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--driver", default="")
    parser.add_argument("--slices", type=int, default=4)
    parser.add_argument("--cap-min", type=float, default=120)
    parser.add_argument("--budget-min", type=float, default=0, help="no step starts after this many minutes")
    parser.add_argument("--label", default="")
    options = parser.parse_args()
    lower_priority()
    root = os.path.abspath(options.root)
    export = os.path.abspath(options.export)
    folder = os.path.join(os.path.dirname(os.path.abspath(options.out)), "replay-work")
    os.makedirs(folder, exist_ok=True)
    record = {"label": options.label, "export": export, "export_bytes": os.path.getsize(export), "steps": {}}
    if os.path.isfile(options.out):
        with open(options.out, encoding="utf-8") as handle:
            record = json.load(handle)
    theorems = [t for t in options.theorems.split(",") if t]
    env = lean_env(root, [])
    cap = options.cap_min * 60
    began = time.monotonic()
    for step in [s for s in options.steps.split(",") if s]:
        if options.budget_min and time.monotonic() - began > options.budget_min * 60:
            record["steps"][step] = {"skipped": "budget"}
            continue
        print(f"--- {step}", flush=True)
        if step == "driver-profile":
            times = os.path.join(folder, "times.tsv")
            row = capped([options.driver, "profile", export, "--out", times], env=env, cwd=folder, timeout=cap)
            row["result"] = row.pop("json", None)
            row["times"] = summarize_times(times)
        elif step == "driver-slices":
            times = os.path.join(folder, "times.tsv")
            rows = []
            for index in range(options.slices):
                cmd = [options.driver, "slice", export, "--slices", str(options.slices), "--index", str(index)]
                if os.path.isfile(times):
                    cmd += ["--weights", times]
                part = capped(cmd, env=env, cwd=folder, timeout=cap)
                part["result"] = part.pop("json", None)
                rows.append(part)
                print(json.dumps({k: part.get(k) for k in ("exit", "wall_s", "peak_rss")}), (part["says"] or "")[-300:],
                      flush=True)
            row = {"slices": rows, "exit": max((r.get("exit") or 0) for r in rows) if rows else None}
        elif step.startswith("comparator"):
            nanoda = step == "comparator-nanoda"
            config = comparator_config(os.path.join(folder, f"{step}.json"), theorems, nanoda=nanoda)
            sandbox = step == "comparator-bwrap" and IS_LINUX
            cmd = comparator_command(root, config, os.path.abspath(options.challenge), export, sandbox=sandbox,
                                     paranoid=step == "comparator-paranoid")
            row = capped(cmd, env=env, cwd=folder, timeout=cap * (4 if step == "comparator-paranoid" else 1))
        else:
            row = capped(kernel_command(root, step, export, folder), env=env, cwd=folder, timeout=cap)
        record["steps"][step] = row
        print(json.dumps({k: row.get(k) for k in ("exit", "wall_s", "user_s", "peak_rss", "killed")}),
              (row.get("says") or "")[-300:], flush=True)
        write_json(options.out, record)
    write_json(options.out, record)
    return 0


def last_json(row: dict):
    for part in reversed((row.get("says") or "").split(" / ")):
        part = part.strip()
        if part.startswith("{"):
            try:
                return json.loads(part)
            except ValueError:
                return None
    return None


def summarize_times(path: str) -> dict:
    """The distribution of the per-declaration kernel times: what bounds a split."""
    if not os.path.isfile(path):
        return {}
    times = []
    by_kind = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if len(parts) != 3:
                continue
            us = int(parts[2])
            times.append((us, parts[0]))
            entry = by_kind.setdefault(parts[1], [0, 0])
            entry[0] += 1
            entry[1] += us
    if not times:
        return {}
    times.sort(reverse=True)
    total = sum(t for t, _ in times)
    top = times[:15]
    shares = {}
    for n in (1, 10, 100, 1000):
        shares[f"top{n}"] = round(sum(t for t, _ in times[:n]) / total, 4)
    return {"declarations": len(times), "total_s": round(total / 1e6, 1), "max_s": round(times[0][0] / 1e6, 2),
            "median_us": times[len(times) // 2][0], "shares": shares,
            "slowest": [{"name": n, "s": round(t / 1e6, 2)} for t, n in top],
            "by_kind": {k: {"count": v[0], "s": round(v[1] / 1e6, 1)} for k, v in by_kind.items()}}


if __name__ == "__main__":
    sys.exit(main())
