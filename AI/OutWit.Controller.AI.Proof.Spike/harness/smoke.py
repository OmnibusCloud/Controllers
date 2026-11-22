"""PF-0 spike: the measuring prelude compiled, and the smoke sets (items written for the gate).

    smoke.py prelude --lean-root <toolchain root> --work <dir> --out <results dir>
        compiles lean/PF0.lean into <work>/prelude/PF0.olean with the toolchain's own `lean`

    smoke.py run --set core|mathlib --lean-root <toolchain root> --work <dir> --out <results dir>
                 [--repl <repl>] [--jobs N]
        checks a smoke set (one item or more per verdict class) through the fresh-process path
        and, with a REPL, through the pool path too; compares each verdict with the one the
        item expects and the two paths with each other. `core` needs no Mathlib.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys

from pf0lib import EXE, read_jsonl, run_measured, write_json

HERE = os.path.dirname(os.path.abspath(__file__))
SPIKE = os.path.dirname(HERE)


def prelude(options: argparse.Namespace) -> int:
    lean = os.path.join(options.lean_root, "bin", "lean" + EXE)
    folder = os.path.join(options.work, "prelude")
    os.makedirs(folder, exist_ok=True)
    # `lean` names a module by its path under the root folder (the working folder).
    shutil.copyfile(os.path.join(SPIKE, "lean", "PF0.lean"), os.path.join(folder, "PF0.lean"))
    out = run_measured([lean, "-o", "PF0.olean", "PF0.lean"], cwd=folder, timeout=600)
    sys.stdout.write(out["stdout"])
    sys.stderr.write(out["stderr"])
    if out["exit"] != 0 or not os.path.isfile(os.path.join(folder, "PF0.olean")):
        print(f"the prelude did not compile (exit {out['exit']})")
        return 1
    record = {"folder": folder, "wall_s": out["wall_s"], "peak_rss": out["peak_rss"],
              "olean_bytes": os.path.getsize(os.path.join(folder, "PF0.olean"))}
    write_json(os.path.join(options.out, "prelude.json"), record)
    print(f"prelude compiled in {record['wall_s']} s, peak {record['peak_rss'] >> 20} MiB")
    return 0


def run(options: argparse.Namespace) -> int:
    lean = os.path.join(options.lean_root, "bin", "lean" + EXE)
    items_path = os.path.join(SPIKE, "golden", f"smoke-{options.set}.jsonl")
    common = ["--lean-path", os.path.join(options.work, "prelude"), "--items", items_path]
    if options.set == "mathlib":
        common += ["--lean-path-file", os.path.join(options.work, "leanpath.json"), "--import", "Mathlib"]
    fresh_path = os.path.join(options.out, f"smoke-{options.set}.jsonl")
    code = subprocess.run([sys.executable, os.path.join(HERE, "check.py"), "--lean", lean, "--out", fresh_path,
                           "--work", os.path.join(options.work, f"smoke-{options.set}"),
                           "--jobs", str(options.jobs)] + common).returncode
    if code != 0:
        return code
    fresh = {row["id"]: row for row in read_jsonl(fresh_path)}
    pooled = {}
    if options.repl:
        pool_path = os.path.join(options.out, f"smoke-{options.set}-pool.jsonl")
        subprocess.run([sys.executable, os.path.join(HERE, "pool.py"), "--repl", options.repl,
                        "--bin", os.path.join(options.lean_root, "bin"), "--out", pool_path,
                        "--work", os.path.join(options.work, f"smoke-{options.set}-pool"),
                        "--workers", str(options.jobs), "--hard-cap", "120"] + common)
        if os.path.isfile(pool_path):
            pooled = {row["id"]: row for row in read_jsonl(pool_path)}

    lines = [f"### Smoke set `{options.set}`", "",
             "| item | expected | fresh | heartbeats | pool | heartbeats | axioms | fresh s | peak MiB | private MiB |",
             "|---|---|---|---|---|---|---|---|---|---|"]
    wrong = 0
    for item in read_jsonl(items_path):
        row = fresh.get(item["id"], {})
        other = pooled.get(item["id"], {})
        verdict = row.get("verdict") or row.get("status")
        pool_verdict = (other.get("verdict") or other.get("status") or "") if other else ""
        differs = item["expect"] not in ("*", verdict)
        wrong += 1 if differs else 0
        lines.append(
            f"| {item['id']} | {item['expect']} | {verdict}{' **differs**' if differs else ''} | "
            f"{row.get('heartbeats')} | {pool_verdict}{' **differs**' if pool_verdict and pool_verdict != verdict else ''} | "
            f"{other.get('heartbeats', '')} | {' '.join(row.get('axioms') or [])} | {row.get('wall_s')} | "
            f"{(row.get('peak_rss') or 0) >> 20} | {(row.get('peak_private') or 0) >> 20} |")
        if differs and row.get("first_error"):
            shown = " ".join(row["first_error"][:200].split()).replace("|", "/").replace("`", "'")
            lines.append(f"| | | `{shown}` | | | | | | | |")
    text = "\n".join(lines) + "\n"
    print(text)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(text + "\n")
    return 1 if wrong else 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["prelude", "run"])
    parser.add_argument("--set", default="core", choices=["core", "mathlib"])
    parser.add_argument("--lean-root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--repl", default="")
    parser.add_argument("--jobs", type=int, default=2)
    options = parser.parse_args()
    os.makedirs(options.out, exist_ok=True)
    return prelude(options) if options.action == "prelude" else run(options)


if __name__ == "__main__":
    sys.exit(main())
