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
import os
import shlex
import sys
import time

from pf0lib import JsonlWriter, assemble, classify, lean_environment, parse_messages, read_jsonl, run_measured


def check_one(item: dict, options: argparse.Namespace, env: dict) -> dict:
    text, spans = assemble(item, options.imports + ["PF0"])
    path = os.path.join(options.work, f"{item['id']}.lean")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)
    heartbeats = item.get("max_heartbeats") or options.max_heartbeats
    cmd = list(options.wrap) + [options.lean, "--json", f"-DmaxHeartbeats={heartbeats}",
                                f"-DElab.async={options.elab_async}"]
    if options.threads is not None:
        cmd.append(f"--threads={options.threads}")
    cmd.append(path)
    cap = item.get("hard_cap_s") or options.hard_cap
    memory_cap = item.get("memory_mb")
    out = run_measured(cmd, env=env, cwd=options.work, timeout=cap,
                       memory_cap=memory_cap * (1 << 20) if memory_cap else None)
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
    if not options.keep:
        try:
            os.remove(path)
        except OSError:
            pass
    return row


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lean", required=True)
    parser.add_argument("--lean-path", action="append", default=[])
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
    options = parser.parse_args()
    options.wrap = shlex.split(options.wrap)
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

    env = lean_environment([os.path.abspath(p) for p in options.lean_path])
    writer = JsonlWriter(options.out)
    started = time.monotonic()
    counts = {}
    done = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=options.jobs) as pool:
        for row in pool.map(lambda item: check_one(item, options, env), items):
            writer.write(row)
            key = row["verdict"] or row["status"]
            counts[key] = counts.get(key, 0) + 1
            done += 1
            if done % 25 == 0 or done == len(items):
                print(f"{done}/{len(items)} in {time.monotonic() - started:.0f} s: {counts}", flush=True)
    writer.close()
    elapsed = time.monotonic() - started
    rate = 60.0 * len(items) / elapsed if elapsed > 0 else 0.0
    print(f"fresh path: {len(items)} items, {options.jobs} at a time, {elapsed:.1f} s, "
          f"{rate:.1f} items per minute: {counts}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
