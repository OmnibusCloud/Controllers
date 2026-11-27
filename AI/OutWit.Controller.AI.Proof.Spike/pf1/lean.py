"""PF-1 spike: a Lean release by version, and the fast driver built with it.

    lean.py fetch  --version 4.36.0 --rid linux-x64 --dest <dir> --record <json> --env-name PIN_ROOT
    lean.py driver --root <lean root> --dest <dir> --record <json> --env-name DRIVER

`fetch` downloads the official release, checks it against the pinned digest and unpacks it; under
GitHub Actions it exports the toolchain's root under the given name. `driver` copies the driver's
sources into a folder of its own and builds them with that toolchain's `lake` (no network: the
driver needs nothing but the toolchain), and exports the executable's path.
"""
from __future__ import annotations

import argparse
import os
import shutil
import sys

from common import EXE, fetch_lean, github_env, lean_env, run_measured, sha256_file, tool, write_json

HERE = os.path.dirname(os.path.abspath(__file__))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("step", choices=["fetch", "driver"])
    parser.add_argument("--version", default="")
    parser.add_argument("--rid", default="")
    parser.add_argument("--root", default="")
    parser.add_argument("--dest", required=True)
    parser.add_argument("--record", required=True)
    parser.add_argument("--env-name", required=True)
    options = parser.parse_args()
    if options.step == "fetch":
        record = fetch_lean(options.version, options.rid, os.path.abspath(options.dest))
        write_json(options.record, record)
        github_env(options.env_name, record["root"])
        print(record["root"])
        return 0
    root = os.path.abspath(options.root)
    dest = os.path.abspath(options.dest)
    if os.path.isdir(dest):
        shutil.rmtree(dest)
    os.makedirs(dest)
    for name in ("ProofDriver.lean", "lakefile.toml"):
        shutil.copy(os.path.join(HERE, "driver", name), os.path.join(dest, name))
    row = run_measured([tool(root, "lake"), "build"], env=lean_env(root), cwd=dest, timeout=1800)
    exe = os.path.join(dest, ".lake", "build", "bin", "proofdriver" + EXE)
    record = {"root": root, "exit": row["exit"], "wall_s": row["wall_s"], "peak_rss": row["peak_rss"],
              "tail": (row["stdout"] + row["stderr"]).strip().splitlines()[-30:]}
    if os.path.isfile(exe):
        record["exe"] = exe.replace("\\", "/")
        record["bytes"] = os.path.getsize(exe)
        record["sha256"] = sha256_file(exe)
        github_env(options.env_name, record["exe"])
    write_json(options.record, record)
    print("\n".join(record["tail"]))
    return 0 if os.path.isfile(exe) else 1


if __name__ == "__main__":
    sys.exit(main())
