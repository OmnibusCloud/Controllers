"""PF-0 spike: the measuring prelude compiled, and a smoke run that needs no Mathlib.

    smoke.py --lean-root <toolchain root> --work <dir> --out <results dir>

Compiles lean/PF0.lean into <work>/prelude/PF0.olean with the toolchain's own `lean`, then
checks the core items (one per verdict class) through the fresh-process path and compares each
verdict with the one the item expects.
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys

from pf0lib import EXE, read_jsonl, run_measured, write_json

HERE = os.path.dirname(os.path.abspath(__file__))
SPIKE = os.path.dirname(HERE)


def build_prelude(lean: str, work: str) -> dict:
    folder = os.path.join(work, "prelude")
    os.makedirs(folder, exist_ok=True)
    out = run_measured([lean, "-o", os.path.join(folder, "PF0.olean"),
                        os.path.join(SPIKE, "lean", "PF0.lean")], cwd=folder, timeout=600)
    sys.stdout.write(out["stdout"])
    sys.stderr.write(out["stderr"])
    if out["exit"] != 0 or not os.path.isfile(os.path.join(folder, "PF0.olean")):
        raise SystemExit(f"the prelude did not compile (exit {out['exit']})")
    return {"folder": folder, "wall_s": out["wall_s"], "peak_rss": out["peak_rss"],
            "olean_bytes": os.path.getsize(os.path.join(folder, "PF0.olean"))}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lean-root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    options = parser.parse_args()
    lean = os.path.join(options.lean_root, "bin", "lean" + EXE)
    os.makedirs(options.out, exist_ok=True)

    prelude = build_prelude(lean, options.work)
    print(f"prelude compiled in {prelude['wall_s']} s, peak {prelude['peak_rss'] >> 20} MiB")
    write_json(os.path.join(options.out, "prelude.json"), prelude)

    items_path = os.path.join(SPIKE, "golden", "smoke-core.jsonl")
    rows_path = os.path.join(options.out, "smoke-core.jsonl")
    code = subprocess.run([sys.executable, os.path.join(HERE, "check.py"), "--lean", lean,
                           "--lean-path", prelude["folder"], "--items", items_path,
                           "--out", rows_path, "--work", os.path.join(options.work, "smoke-core"),
                           "--jobs", "2"]).returncode
    if code != 0:
        return code
    expected = {item["id"]: item["expect"] for item in read_jsonl(items_path)}
    lines = ["### Core smoke (no Mathlib)", "",
             "| item | expected | verdict | heartbeats | axioms | wall s | peak MiB |", "|---|---|---|---|---|---|---|"]
    wrong = 0
    for row in read_jsonl(rows_path):
        verdict = row["verdict"] or row["status"]
        mark = "" if verdict == expected[row["id"]] else " **differs**"
        wrong += 1 if mark else 0
        lines.append(f"| {row['id']} | {expected[row['id']]} | {verdict}{mark} | {row['heartbeats']} | "
                     f"{' '.join(row['axioms'] or [])} | {row['wall_s']} | {row['peak_rss'] >> 20} |")
        if mark and row.get("first_error"):
            shown = " ".join(row["first_error"][:160].split()).replace("|", "/").replace("`", "'")
            lines.append(f"| | | `{shown}` | | | | |")
    text = "\n".join(lines) + "\n"
    print(text)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(text + "\n")
    return 1 if wrong else 0


if __name__ == "__main__":
    sys.exit(main())
