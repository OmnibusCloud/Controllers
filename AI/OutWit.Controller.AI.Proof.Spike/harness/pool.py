"""PF-0 spike: the pool path. Long-lived REPL processes, each importing the environment once.

    pool.py --repl <repl> --bin <toolchain bin> --lean-path-file <json> [--lean-path <dir>]...
            --items items.jsonl --out results.jsonl [--import Mathlib]... [--workers N]
            [--max-heartbeats N] [--hard-cap S] [--recycle K] [--every K] [--limit N]

Every worker starts one REPL (leanprover-community/repl), sends the import lines as its first
command and then every item as one command against that first environment (the REPL forks the
state: no item sees another). The item text is the very text the fresh-process path elaborates
after its import lines. A process that does not answer inside the hard cap is killed and
replaced. Recorded per item: verdict, heartbeats, time from request to answer; per process: the
time to import, and resident and private memory after the import and as items go by.
"""
from __future__ import annotations

import argparse
import json
import os
import queue
import subprocess
import sys
import threading
import time

from pf0lib import JsonlWriter, assemble_body, classify, lean_environment, live_memory, read_jsonl, write_json


class Repl:
    """One REPL process: a command is one JSON line and a blank line; the answer is JSON
    followed by a blank line."""

    def __init__(self, path: str, env: dict, cwd: str):
        self.proc = subprocess.Popen([path], env=env, cwd=cwd, stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        self.lines = queue.Queue()
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()

    def _read(self) -> None:
        for raw in self.proc.stdout:
            self.lines.put(raw.decode("utf-8", errors="replace"))
        self.lines.put(None)

    def ask(self, request: dict, timeout: float) -> dict | None:
        """The answer, or None when the process died or did not answer in time."""
        try:
            # Sent as UTF-8, not as \u escapes: a character outside the basic plane would arrive
            # as two surrogate escapes.
            self.proc.stdin.write((json.dumps(request, ensure_ascii=False) + "\n\n").encode("utf-8"))
            self.proc.stdin.flush()
        except OSError:
            return None
        deadline = time.monotonic() + timeout
        collected = []
        while True:
            try:
                line = self.lines.get(timeout=max(0.01, deadline - time.monotonic()))
            except queue.Empty:
                return None
            if line is None:
                return None
            if line.strip():
                collected.append(line)
            elif collected:
                try:
                    return json.loads("".join(collected))
                except ValueError:
                    return {"message": "unparseable answer: " + "".join(collected)[:400]}
            if time.monotonic() > deadline:
                return None

    def memory(self) -> dict:
        return live_memory(self.proc)

    def close(self) -> None:
        try:
            self.proc.kill()
        except OSError:
            pass
        try:
            self.proc.wait(timeout=30)
        except subprocess.SubprocessError:
            pass


def to_lean_messages(answer: dict) -> list:
    """The REPL's messages in the shape of `lean --json`."""
    names = {"info": "information", "warning": "warning", "error": "error", "trace": "information"}
    return [{"severity": names.get(m.get("severity"), m.get("severity")), "pos": m.get("pos"),
             "data": m.get("data", "")} for m in answer.get("messages", [])]


def worker(index: int, options: argparse.Namespace, env: dict, items: "queue.Queue", writer: JsonlWriter,
           processes: list, lock: threading.Lock) -> None:
    repl = None
    served = 0
    record = None
    header = "".join(f"import {name}\n" for name in options.imports + ["PF0"])

    def start() -> bool:
        nonlocal repl, served, record
        began = time.monotonic()
        repl = Repl(options.repl, env, options.work)
        answer = repl.ask({"cmd": header}, 1800)
        took = time.monotonic() - began
        record = {"worker": index, "import_s": round(took, 3), "after_import": repl.memory(), "samples": []}
        with lock:
            processes.append(record)
        served = 0
        if answer is None or "env" not in answer or any(
                m.get("severity") == "error" for m in answer.get("messages", [])):
            record["import_failed"] = json.dumps(answer)[:600] if answer else "no answer"
            return False
        record["env"] = answer["env"]
        return True

    if not start():
        repl.close()
        return
    while True:
        try:
            item = items.get_nowait()
        except queue.Empty:
            break
        if options.recycle and served >= options.recycle:
            record["at_end"] = repl.memory()
            record["served"] = served
            repl.close()
            if not start():
                break
        heartbeats = item.get("max_heartbeats") or options.max_heartbeats
        body, spans = assemble_body(item, options.max_heartbeats)
        cap = item.get("hard_cap_s") or options.hard_cap
        began = time.monotonic()
        answer = repl.ask({"cmd": body, "env": record["env"]}, cap)
        took = time.monotonic() - began
        served += 1
        if answer is None:
            row = {"verdict": "timeout" if took >= cap else None,
                   "status": "ok" if took >= cap else "engine_crash"}
            record["at_end"] = repl.memory()
            record["served"] = served
            repl.close()
            if not start():
                writer.write(dict(row, id=item["id"], path="pool", worker=index, wall_s=round(took, 4)))
                break
        elif "message" in answer and "messages" not in answer:
            row = {"verdict": None, "status": "repl_error", "first_error": str(answer["message"])[:240]}
        else:
            row = classify(item, to_lean_messages(answer), spans, allow_native=options.allow_native)
        row.update({"id": item["id"], "path": "pool", "worker": index, "max_heartbeats": heartbeats,
                    "wall_s": round(took, 4)})
        for key in ("expect", "class", "made"):
            if key in item:
                row[key] = item[key]
        writer.write(row)
        if served % options.sample_every == 0:
            sample = repl.memory()
            sample["served"] = served
            record["samples"].append(sample)
    if repl is not None and repl.proc.poll() is None:
        record["at_end"] = repl.memory()
        record["served"] = served
        repl.close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repl", required=True)
    parser.add_argument("--bin", required=True)
    parser.add_argument("--lean-path", action="append", default=[])
    parser.add_argument("--lean-path-file", default="")
    parser.add_argument("--items", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--import", dest="imports", action="append", default=[])
    parser.add_argument("--workers", type=int, default=1)
    parser.add_argument("--max-heartbeats", type=int, default=200000)
    parser.add_argument("--hard-cap", type=float, default=300.0)
    parser.add_argument("--recycle", type=int, default=0)
    parser.add_argument("--sample-every", type=int, default=10)
    parser.add_argument("--allow-native", action="store_true")
    parser.add_argument("--every", type=int, default=1)
    parser.add_argument("--limit", type=int, default=0)
    options = parser.parse_args()
    options.work = os.path.abspath(options.work)
    os.makedirs(options.work, exist_ok=True)
    if options.lean_path_file:
        with open(options.lean_path_file, encoding="utf-8") as handle:
            options.lean_path = json.load(handle) + options.lean_path

    items = [item for item in read_jsonl(options.items) if not item.get("memory_mb")]
    if options.every > 1:
        items = items[::options.every]
    if options.limit:
        items = items[:options.limit]
    todo = queue.Queue()
    for item in items:
        todo.put(item)

    env = lean_environment([os.path.abspath(p) for p in options.lean_path], bin_dir=os.path.abspath(options.bin))
    writer = JsonlWriter(options.out)
    processes = []
    lock = threading.Lock()
    started = time.monotonic()
    threads = [threading.Thread(target=worker, args=(index, options, env, todo, writer, processes, lock))
               for index in range(options.workers)]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()
    elapsed = time.monotonic() - started
    writer.close()

    rows = read_jsonl(options.out)
    counts = {}
    for row in rows:
        key = row.get("verdict") or row.get("status")
        counts[key] = counts.get(key, 0) + 1
    imports = [p["import_s"] for p in processes]
    after_import = max(imports) if imports else 0.0
    summary = {
        "items": len(rows), "workers": options.workers, "elapsed_s": round(elapsed, 2),
        "items_per_minute": round(60.0 * len(rows) / elapsed, 2) if elapsed > 0 else 0.0,
        "items_per_minute_after_import": round(60.0 * len(rows) / (elapsed - after_import), 2)
        if elapsed > after_import else 0.0,
        "verdicts": counts, "processes": processes,
    }
    write_json(os.path.splitext(options.out)[0] + "-summary.json", summary)
    print(f"pool path: {len(rows)} items, {options.workers} processes, {elapsed:.1f} s, "
          f"{summary['items_per_minute']} items per minute "
          f"({summary['items_per_minute_after_import']} after the import): {counts}")
    for process in processes:
        print(json.dumps({key: process.get(key) for key in ("worker", "import_s", "after_import", "at_end", "served", "import_failed")}))
    return 0 if rows and not any(p.get("import_failed") for p in processes) else 1


if __name__ == "__main__":
    sys.exit(main())
