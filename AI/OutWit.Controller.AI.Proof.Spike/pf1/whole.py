"""PF-1 spike: the export of all of Mathlib at the profile's revision (the largest closure a project
on it can carry), made on a hosted runner, its size and cost, then replayed as replay.py replays a kit.

    whole.py --root <lean root> --work <dir> --out <dir>

Writes <work>/mathlib.ndjson and <out>/whole.json (the export's bytes, lines, digest, meta line,
seconds and peak memory, zstd -3 size).
"""
from __future__ import annotations

import argparse
import os
import sys

from common import compress, lean_env, load_pins, read_export_summary, run_measured, tool, write_json
from profile import clone, lake_env_path, short


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--rev", default="fc4f59e577feb1a4956671bc04ac18aaaf8cd842")
    options = parser.parse_args()
    pins = load_pins()
    root = os.path.abspath(options.root)
    work = os.path.abspath(options.work)
    out = os.path.abspath(options.out)
    os.makedirs(out, exist_ok=True)
    source = os.path.join(work, "mathlib4")
    env = lean_env(root, extra={"MATHLIB_CACHE_DIR": os.path.join(work, "mathlib-cache"), "GIT_TERMINAL_PROMPT": "0"})
    record = {"rev": options.rev}
    record.update(clone(pins["MATHLIB_REPO"], options.rev, source))
    row = run_measured([tool(root, "lake"), "exe", "cache", "get"], env=env, cwd=source, timeout=3600)
    record["cache_get"] = short(row)
    if row["exit"] != 0:
        write_json(os.path.join(out, "whole.json"), record)
        return 1
    lean_path = lake_env_path(root, source, env)
    target = os.path.join(work, "mathlib.ndjson")
    print("--- leanexport Mathlib (every non-internal constant)", flush=True)
    row = run_measured([tool(root, "leanexport"), "Mathlib"], env=lean_env(root, lean_path), cwd=source,
                       timeout=3 * 3600, stdout_path=target)
    record["export"] = short(row)
    if row["exit"] == 0:
        record["export"].update(read_export_summary(target))
        record["export"]["zstd3"] = compress(target, os.path.join(work, "mathlib.ndjson.zst"), 3)
        os.remove(os.path.join(work, "mathlib.ndjson.zst"))
    write_json(os.path.join(out, "whole.json"), record)
    print({k: record["export"].get(k) for k in ("exit", "wall_s", "peak_rss", "bytes", "lines", "sha256", "zstd3")})
    return 0 if row["exit"] == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
