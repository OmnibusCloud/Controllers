"""PF-0 spike: the fresh-process path. One `lean` process per item.

    check.py --lean <lean> --lean-path <dir>... --items items.jsonl --out results.jsonl
             --work <dir> [--import Mathlib]... [--jobs N] [--max-heartbeats N]
             [--hard-cap S] [--async true|false] [--threads N] [--wrap "prog args.."]
             [--only id,id] [--every K] [--limit N]

Every item is assembled into one file (the profile's imports, the measuring prelude, the item's
`open` lines, the challenge, the solution) and elaborated by its own process with parallel
elaboration off, so that the heartbeats of the solution are counted on one thread. A row per
item: verdict, heartbeats, axioms, hashes of statement and proof term, time, peak memory.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import json
import os
import shlex
import sys
import time

from pf0lib import (EXE, JsonlWriter, assemble, classify, lean_environment, parse_messages, read_jsonl,
                    run_measured, write_json)


def check_one(item: dict, options: argparse.Namespace, env: dict) -> dict:
    heartbeats = item.get("max_heartbeats") or options.max_heartbeats
    text, spans = assemble(item, options.imports + ["PF0"], options.max_heartbeats)
    name = f"{item['id']}.lean"
    path = os.path.join(options.work, name)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)
    cmd = list(options.wrap) + [options.lean, "--json", f"-DElab.async={options.elab_async}"]
    if options.threads is not None:
        cmd.append(f"--threads={options.threads}")
    cmd.append(name)
    cap = item.get("hard_cap_s") or options.hard_cap
    memory_cap = item.get("memory_mb")
    out = run_measured(cmd, env=env, cwd=options.work, timeout=cap,
                       memory_cap=memory_cap * (1 << 20) if memory_cap else None,
                       sample=options.sample)
    messages = parse_messages(out["stdout"])
    row = classify(item, messages, spans, allow_native=options.allow_native)
    if out["killed"] == "timeout":
        row["verdict"], row["status"] = "timeout", "ok"
    elif out["killed"] == "memory":
        row["verdict"], row["status"] = "memout", "ok"
    elif row["status"] == "no_report":
        row["status"] = "engine_crash" if out["exit"] not in (0, 1) else "no_report"
        row["first_error"] = (out["stderr"] or out["stdout"])[-240:]
    row.update({
        "id": item["id"], "path": "fresh", "max_heartbeats": heartbeats, "exit": out["exit"],
        "wall_s": out["wall_s"], "user_s": out["user_s"], "sys_s": out["sys_s"],
        "peak_rss": out["peak_rss"], "messages": len(messages),
    })
    if "peak_private" in out:
        row["peak_private"] = out["peak_private"]
    for key in ("expect", "class", "made"):
        if key in item:
            row[key] = item[key]
    if not options.keep:
        try:
            os.remove(path)
        except OSError:
            pass
    return row


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lean", default="")
    parser.add_argument("--lean-root", default="", help="the toolchain root (instead of --lean)")
    parser.add_argument("--lean-path", action="append", default=[])
    parser.add_argument("--lean-path-file", default="", help="a JSON list of search folders")
    parser.add_argument("--items", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--import", dest="imports", action="append", default=[])
    parser.add_argument("--jobs", type=int, default=1)
    parser.add_argument("--max-heartbeats", type=int, default=200000)
    parser.add_argument("--hard-cap", type=float, default=300.0)
    parser.add_argument("--async", dest="elab_async", default="false", choices=["true", "false"])
    parser.add_argument("--threads", type=int, default=None)
    parser.add_argument("--wrap", default="")
    parser.add_argument("--allow-native", action="store_true")
    parser.add_argument("--only", default="")
    parser.add_argument("--every", type=int, default=1)
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--sample", type=float, default=0.25,
                        help="seconds between samples of the private memory (0: none)")
    parser.add_argument("--bin", default="", help="the toolchain's bin folder, put first on PATH")
    options = parser.parse_args()
    options.wrap = shlex.split(options.wrap)
    if not options.lean:
        options.lean = os.path.join(os.path.abspath(options.lean_root), "bin", "lean" + EXE)
    options.work = os.path.abspath(options.work)
    os.makedirs(options.work, exist_ok=True)

    items = read_jsonl(options.items)
    if options.only:
        wanted = set(options.only.split(","))
        items = [item for item in items if item["id"] in wanted]
    if options.every > 1:
        items = items[::options.every]
    if options.limit:
        items = items[:options.limit]

    if options.lean_path_file:
        with open(options.lean_path_file, encoding="utf-8") as handle:
            options.lean_path = json.load(handle) + options.lean_path
    env = lean_environment([os.path.abspath(p) for p in options.lean_path],
                           bin_dir=options.bin or os.path.dirname(os.path.abspath(options.lean)))
    writer = JsonlWriter(options.out)
    started = time.monotonic()
    counts = {}
    done = 0
    peaks = []
    privates = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=options.jobs) as pool:
        for row in pool.map(lambda item: check_one(item, options, env), items):
            writer.write(row)
            key = row["verdict"] or row["status"]
            counts[key] = counts.get(key, 0) + 1
            peaks.append(row.get("peak_rss") or 0)
            privates.append(row.get("peak_private") or 0)
            done += 1
            if done % 25 == 0 or done == len(items):
                print(f"{done}/{len(items)} in {time.monotonic() - started:.0f} s: {counts}", flush=True)
    writer.close()
    elapsed = time.monotonic() - started
    rate = 60.0 * len(items) / elapsed if elapsed > 0 else 0.0
    peaks.sort()
    privates.sort()
    write_json(os.path.splitext(options.out)[0] + "-summary.json", {
        "items": len(items), "jobs": options.jobs, "elapsed_s": round(elapsed, 2),
        "items_per_minute": round(rate, 2), "verdicts": counts, "wrapped": bool(options.wrap),
        "elab_async": options.elab_async, "threads": options.threads,
        "peak_rss_median": peaks[len(peaks) // 2] if peaks else 0, "peak_rss_max": peaks[-1] if peaks else 0,
        "peak_private_median": privates[len(privates) // 2] if privates else 0,
        "peak_private_max": privates[-1] if privates else 0})
    print(f"fresh path: {len(items)} items, {options.jobs} at a time, {elapsed:.1f} s, "
          f"{rate:.1f} items per minute: {counts}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
