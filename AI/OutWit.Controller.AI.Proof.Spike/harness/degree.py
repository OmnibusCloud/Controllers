"""PF-0 spike: how many checking processes fit a machine.

    degree.py --repl <repl> --bin <toolchain bin> --lean-path-file <json> [--lean-path <dir>]...
              --work <dir> --out <degree.json> [--import Mathlib]... [--max N] [--reserve-mb M]
              [--items items.jsonl] [--per-process K]

Starts pooled processes one after another, each importing the environment, and after each one
records what the whole machine has left and what each process holds (resident, private). It
stops at --max processes or when the machine has less than the reserve left. With items, every
process then checks a few and the memory is read once more. The step from one process to the
next is the marginal cost of a process: the compiled files are mapped once for all of them.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time

from pf0lib import assemble_body, lean_environment, read_jsonl, system_memory, write_json
from pool import Repl


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repl", required=True)
    parser.add_argument("--bin", required=True)
    parser.add_argument("--lean-path", action="append", default=[])
    parser.add_argument("--lean-path-file", default="")
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--import", dest="imports", action="append", default=[])
    parser.add_argument("--max", type=int, default=6)
    parser.add_argument("--reserve-mb", type=int, default=1536)
    parser.add_argument("--items", default="")
    parser.add_argument("--per-process", type=int, default=3)
    parser.add_argument("--max-heartbeats", type=int, default=400000)
    options = parser.parse_args()
    work = os.path.abspath(options.work)
    os.makedirs(work, exist_ok=True)
    if options.lean_path_file:
        with open(options.lean_path_file, encoding="utf-8") as handle:
            options.lean_path = json.load(handle) + options.lean_path
    env = lean_environment([os.path.abspath(p) for p in options.lean_path], bin_dir=os.path.abspath(options.bin))
    header = "".join(f"import {name}\n" for name in options.imports + ["PF0"])

    record = {"baseline": system_memory(), "steps": [], "stopped": None}
    print(f"baseline: {record['baseline']}", flush=True)
    processes = []
    for count in range(1, options.max + 1):
        left = system_memory().get("available", 0)
        if count > 1 and left < options.reserve_mb << 20:
            record["stopped"] = f"{left >> 20} MiB left before process {count}"
            break
        began = time.monotonic()
        repl = Repl(options.repl, env, work)
        answer = repl.ask({"cmd": header}, 1800)
        took = time.monotonic() - began
        if answer is None or "env" not in answer:
            record["stopped"] = f"process {count} did not import: {json.dumps(answer)[:300] if answer else 'no answer'}"
            repl.close()
            break
        processes.append((repl, answer["env"]))
        time.sleep(1.0)
        step = {"processes": count, "import_s": round(took, 3), "system": system_memory(),
                "each": [p.memory() for p, _env in processes]}
        record["steps"].append(step)
        print(f"{count} processes: import {took:.1f} s, system {step['system']}, last {step['each'][-1]}", flush=True)

    if options.items and processes:
        items = [item for item in read_jsonl(options.items)
                 if not item.get("memory_mb") and not item.get("hard_cap_s")][:options.per_process * len(processes)]
        done = 0
        for index, item in enumerate(items):
            repl, env_id = processes[index % len(processes)]
            body, _spans = assemble_body(item, options.max_heartbeats)
            if repl.ask({"cmd": body, "env": env_id}, 300) is not None:
                done += 1
        record["after_items"] = {"items": done, "system": system_memory(),
                                 "each": [p.memory() for p, _env in processes]}
        print(f"after {done} items: {record['after_items']['system']}", flush=True)
    for repl, _env in processes:
        repl.close()
    time.sleep(1.0)
    record["after_close"] = system_memory()
    write_json(options.out, record)
    return 0 if record["steps"] else 1


if __name__ == "__main__":
    sys.exit(main())
