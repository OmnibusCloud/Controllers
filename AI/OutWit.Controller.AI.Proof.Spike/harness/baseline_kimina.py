"""PF-0 spike: the Kimina Lean Server as a baseline, on one runner with the gate's own items.

    baseline_kimina.py --server <checkout of kimina-lean-server> --python <its interpreter>
                       --lean-root <toolchain root> --work <dir> --repl <repl> --items items.jsonl
                       --out <results dir> [--repls N] [--batch N] [--every K] [--limit N]

Starts the server (FastAPI over a pool of the same REPL the pool path uses) against the Mathlib
environment of the gate, sends the items in batches to `/api/check` as whole files (the import
lines, then the very text the other paths elaborate) and reads the verdicts back from the
messages. The measuring prelude is made importable by the server's project by copying its
compiled file beside Mathlib's.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

from pf0lib import JsonlWriter, assemble, classify, read_jsonl, write_json
from pool import to_lean_messages


def call(url: str, payload: dict | None, timeout: float) -> dict:
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    request = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", required=True)
    parser.add_argument("--python", required=True)
    parser.add_argument("--lean-root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--repl", required=True)
    parser.add_argument("--items", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--repls", type=int, default=4)
    parser.add_argument("--batch", type=int, default=32)
    parser.add_argument("--max-heartbeats", type=int, default=400000)
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--every", type=int, default=1)
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--port", type=int, default=8000)
    options = parser.parse_args()
    work = os.path.abspath(options.work)
    project = os.path.join(work, "mathlib4")
    os.makedirs(options.out, exist_ok=True)
    shutil.copyfile(os.path.join(work, "prelude", "PF0.olean"),
                    os.path.join(project, ".lake", "build", "lib", "lean", "PF0.olean"))

    items = [item for item in read_jsonl(options.items) if not item.get("memory_mb") and not item.get("hard_cap_s")]
    if options.every > 1:
        items = items[::options.every]
    if options.limit:
        items = items[:options.limit]

    env = dict(os.environ)
    env["PATH"] = os.path.join(os.path.abspath(options.lean_root), "bin") + os.pathsep + env.get("PATH", "")
    env.update({"LEAN_SERVER_REPL_PATH": os.path.abspath(options.repl), "LEAN_SERVER_PROJECT_DIR": project,
                "LEAN_SERVER_MAX_REPLS": str(options.repls), "LEAN_SERVER_PORT": str(options.port),
                "LEAN_SERVER_HOST": "127.0.0.1", "LEAN_SERVER_MAX_WAIT": "3600", "LEAN_SERVER_LOG_LEVEL": "WARNING",
                "LEAN_SERVER_MAX_REPL_MEM": "16G", "LEAN_SERVER_LEAN_VERSION": "v4.36.0"})
    log = open(os.path.join(options.out, "kimina-server.log"), "wb")
    server = subprocess.Popen([options.python, "-m", "server"], cwd=options.server, env=env, stdout=log, stderr=log)
    base = f"http://127.0.0.1:{options.port}"
    record = {"items": len(items), "repls": options.repls, "batch": options.batch}
    try:
        started = time.monotonic()
        up = False
        while time.monotonic() - started < 180 and server.poll() is None:
            try:
                call(base + "/health", None, 5)
                up = True
                break
            except (urllib.error.URLError, OSError, ValueError):
                time.sleep(1)
        record["start_s"] = round(time.monotonic() - started, 2)
        if not up:
            record["error"] = "the server did not come up"
            write_json(os.path.join(options.out, "kimina.json"), record)
            return 1

        writer = JsonlWriter(os.path.join(options.out, "kimina.jsonl"))
        counts = {}
        began = time.monotonic()
        for offset in range(0, len(items), options.batch):
            batch = items[offset:offset + options.batch]
            spans = {}
            snippets = []
            for item in batch:
                text, spans[item["id"]] = assemble(item, ["Mathlib", "PF0"], options.max_heartbeats)
                snippets.append({"id": item["id"], "code": text})
            sent = time.monotonic()
            try:
                answer = call(base + "/api/check", {"snippets": snippets, "timeout": options.timeout, "reuse": True},
                              options.timeout * 4 + 600)
            except (urllib.error.URLError, OSError, ValueError) as error:
                record.setdefault("batch_errors", []).append(str(error)[:300])
                continue
            took = time.monotonic() - sent
            by_id = {item["id"]: item for item in batch}
            for result in answer.get("results", []):
                item = by_id.get(result.get("id"))
                if item is None:
                    continue
                response = result.get("response") or {}
                if result.get("error"):
                    row = {"verdict": "timeout" if "timed out" in result["error"] else None,
                           "status": "ok" if "timed out" in result["error"] else "server_error",
                           "first_error": result["error"][:240]}
                elif "message" in response and "messages" not in response:
                    row = {"verdict": None, "status": "repl_error", "first_error": str(response["message"])[:240]}
                else:
                    row = classify(item, to_lean_messages(response), spans[item["id"]])
                row.update({"id": item["id"], "path": "kimina", "wall_s": result.get("time"),
                            "batch_s": round(took, 3)})
                writer.write(row)
                key = row.get("verdict") or row.get("status")
                counts[key] = counts.get(key, 0) + 1
            print(f"{min(offset + options.batch, len(items))}/{len(items)} in {time.monotonic() - began:.0f} s: {counts}",
                  flush=True)
        elapsed = time.monotonic() - began
        writer.close()
        record.update({"elapsed_s": round(elapsed, 2), "verdicts": counts,
                       "items_per_minute": round(60.0 * sum(counts.values()) / elapsed, 2) if elapsed > 0 else 0.0})
        write_json(os.path.join(options.out, "kimina.json"), record)
        print(json.dumps(record))
        return 0
    finally:
        server.terminate()
        try:
            server.wait(timeout=30)
        except subprocess.TimeoutExpired:
            server.kill()
        log.close()


if __name__ == "__main__":
    sys.exit(main())
