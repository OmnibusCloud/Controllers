"""PF-0 spike: the tools beside the toolchain, each at its pin.

    tools.py repl --lean-root <toolchain root> --work <dir> --out <results dir>
        clones leanprover-community/repl at the pinned tag, checks the commit and builds it
        with the toolchain's own `lake` (the result: <work>/repl/.lake/build/bin/repl)

    tools.py landrun --rid <rid> --work <dir> --out <results dir>
        fetches the pinned landrun release binary of a Linux platform (<work>/landrun/landrun)
"""
from __future__ import annotations

import argparse
import os
import stat
import subprocess
import sys

from pf0lib import EXE, load_pins, rid_key, run_measured, sha256_file, write_json

HERE = os.path.dirname(os.path.abspath(__file__))
SPIKE = os.path.dirname(HERE)


def repl(options: argparse.Namespace, pins: dict) -> int:
    bin_dir = os.path.join(os.path.abspath(options.lean_root), "bin")
    env = dict(os.environ)
    env["PATH"] = bin_dir + os.pathsep + env.get("PATH", "")
    folder = os.path.join(options.work, "repl")
    record = {"tag": pins["REPL_TAG"], "pinned_commit": pins["REPL_COMMIT"]}
    subprocess.run(["git", "clone", "--quiet", "--depth", "1", "--branch", pins["REPL_TAG"],
                    "-c", "advice.detachedHead=false", pins["REPL_REPO"], folder], check=True)
    head = subprocess.run(["git", "-C", folder, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    record["commit"] = head
    if head != pins["REPL_COMMIT"]:
        print(f"REPL {pins['REPL_TAG']} is {head}, pinned {pins['REPL_COMMIT']}", file=sys.stderr)
        return 1
    out = run_measured([os.path.join(bin_dir, "lake" + EXE), "build", "repl"], env=env, cwd=folder, timeout=3600)
    print((out["stdout"] + out["stderr"])[-3000:])
    binary = os.path.join(folder, ".lake", "build", "bin", "repl" + EXE)
    record.update({"build_s": out["wall_s"], "build_exit": out["exit"], "build_peak_rss": out["peak_rss"]})
    if out["exit"] != 0 or not os.path.isfile(binary):
        write_json(os.path.join(options.out, "repl.json"), record)
        return 1
    record["binary"] = binary.replace("\\", "/")
    record["binary_bytes"] = os.path.getsize(binary)
    record["binary_sha256"] = sha256_file(binary)
    write_json(os.path.join(options.out, "repl.json"), record)
    github_env = os.environ.get("GITHUB_ENV")
    if github_env:
        with open(github_env, "a", encoding="utf-8") as handle:
            handle.write(f"PF0_REPL={record['binary']}\n")
    print(f"repl built in {record['build_s']} s: {record['binary']} ({record['binary_bytes']} bytes)")
    return 0


def landrun(options: argparse.Namespace, pins: dict) -> int:
    key = rid_key(options.rid)
    url = pins.get(f"LANDRUN_{key}_URL")
    if not url:
        print(f"no landrun binary for {options.rid}")
        return 0
    folder = os.path.join(options.work, "landrun")
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, "landrun")
    subprocess.run(["curl", "-fsSL", "--retry", "3", "--max-time", "600", "-o", path, url], check=True)
    seen = sha256_file(path)
    if seen != pins[f"LANDRUN_{key}_SHA256"]:
        os.remove(path)
        print(f"HASH MISMATCH landrun: expected {pins[f'LANDRUN_{key}_SHA256']}, got {seen}", file=sys.stderr)
        return 1
    os.chmod(path, os.stat(path).st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    out = run_measured([path, "--version"], timeout=60)
    record = {"version": pins["LANDRUN_VERSION"], "url": url, "sha256": seen, "bytes": os.path.getsize(path),
              "reports": (out["stdout"] + out["stderr"]).strip()[:300], "binary": path}
    write_json(os.path.join(options.out, "landrun.json"), record)
    github_env = os.environ.get("GITHUB_ENV")
    if github_env:
        with open(github_env, "a", encoding="utf-8") as handle:
            handle.write(f"PF0_LANDRUN={path}\n")
    print(f"pinned   {seen}  landrun: {record['reports']}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("tool", choices=["repl", "landrun"])
    parser.add_argument("--lean-root", default="")
    parser.add_argument("--rid", default="")
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    options = parser.parse_args()
    options.work = os.path.abspath(options.work)
    os.makedirs(options.out, exist_ok=True)
    pins = load_pins(os.path.join(SPIKE, "pins.env"))
    return repl(options, pins) if options.tool == "repl" else landrun(options, pins)


if __name__ == "__main__":
    sys.exit(main())
