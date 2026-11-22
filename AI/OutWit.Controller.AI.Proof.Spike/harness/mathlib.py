"""PF-0 spike: the Mathlib environment of the profile, made ready by Mathlib's own cache tool.

    mathlib.py --lean-root <toolchain root> --work <dir> --out <results dir> [--skip-hash]

Clones Mathlib at the pinned tag (one commit deep), checks the commit, lets `lake` resolve the
dependencies and build the cache tool, downloads the compiled files (`lake exe cache get-`),
unpacks them (`lake exe cache unpack`), and records every step: seconds, bytes downloaded, bytes
on disk by kind of file, the search path Lean needs, and a manifest (relative path, size,
SHA-256) of every compiled file with the time hashing them took.
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import subprocess
import sys
import time

from pf0lib import EXE, load_pins, run_measured, sha256_file, tree_bytes, write_json

HERE = os.path.dirname(os.path.abspath(__file__))
SPIKE = os.path.dirname(HERE)


def step(record: dict, name: str, cmd: list, env: dict, cwd: str, timeout: float = 7200) -> dict:
    print(f"--- {name}: {' '.join(cmd)}", flush=True)
    out = run_measured(cmd, env=env, cwd=cwd, timeout=timeout)
    tail = (out["stdout"] + "\n" + out["stderr"]).strip().splitlines()[-25:]
    print("\n".join(tail), flush=True)
    record[name] = {"wall_s": out["wall_s"], "user_s": out["user_s"], "sys_s": out["sys_s"],
                    "peak_rss": out["peak_rss"], "exit": out["exit"]}
    if out["exit"] != 0:
        write_json(record["_path"], record)
        raise SystemExit(f"{name} failed with exit {out['exit']}")
    return out


def kind_of(name: str) -> str:
    for suffix in (".olean.server", ".olean.private", ".olean", ".ilean", ".ir", ".c", ".hash", ".trace"):
        if name.endswith(suffix):
            return suffix
    return os.path.splitext(name)[1] or "(none)"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lean-root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--skip-hash", action="store_true")
    options = parser.parse_args()
    pins = load_pins(os.path.join(SPIKE, "pins.env"))
    work = os.path.abspath(options.work)
    out_dir = os.path.abspath(options.out)
    os.makedirs(out_dir, exist_ok=True)
    bin_dir = os.path.join(os.path.abspath(options.lean_root), "bin")
    lake = os.path.join(bin_dir, "lake" + EXE)
    source = os.path.join(work, "mathlib4")
    cache = os.path.join(work, "mathlib-cache")
    env = dict(os.environ)
    env["PATH"] = bin_dir + os.pathsep + env.get("PATH", "")
    env["MATHLIB_CACHE_DIR"] = cache
    record = {"_path": os.path.join(out_dir, "mathlib.json"), "tag": pins["MATHLIB_TAG"],
              "pinned_commit": pins["MATHLIB_COMMIT"]}

    step(record, "clone", ["git", "clone", "--depth", "1", "--branch", pins["MATHLIB_TAG"],
                           "-c", "advice.detachedHead=false", pins["MATHLIB_REPO"], source], env, work)
    head = subprocess.run(["git", "-C", source, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    record["commit"] = head
    if head != pins["MATHLIB_COMMIT"]:
        write_json(record["_path"], record)
        raise SystemExit(f"Mathlib {pins['MATHLIB_TAG']} is {head}, pinned {pins['MATHLIB_COMMIT']}")
    record["clone_bytes"] = tree_bytes(source)[0]
    with open(os.path.join(source, "lean-toolchain"), encoding="utf-8") as handle:
        record["lean_toolchain"] = handle.read().strip()

    # Without arguments the cache tool prints its help: this step is the dependencies and the tool.
    step(record, "resolve_and_build_tool", [lake, "exe", "cache"], env, source)
    record["packages_bytes"] = tree_bytes(os.path.join(source, ".lake", "packages"))[0]
    manifest = json.load(open(os.path.join(source, "lake-manifest.json"), encoding="utf-8"))
    record["packages"] = {p["name"]: p.get("rev") for p in manifest.get("packages", [])}

    step(record, "download", [lake, "exe", "cache", "get-"], env, source)
    size, files = tree_bytes(cache)
    record["download_bytes"] = size
    record["download_files"] = files
    by_kind = {}
    for folder, _dirs, names in os.walk(cache):
        for name in names:
            kind = os.path.splitext(name)[1] or "(none)"
            entry = by_kind.setdefault(kind, {"bytes": 0, "files": 0})
            entry["bytes"] += os.path.getsize(os.path.join(folder, name))
            entry["files"] += 1
    record["download_by_kind"] = by_kind

    step(record, "unpack", [lake, "exe", "cache", "unpack"], env, source)

    probe = "import json, os; print(json.dumps({k: os.environ.get(k) for k in ('LEAN_PATH', 'LEAN_SYSROOT', 'LEAN_SRC_PATH')}))"
    out = step(record, "lake_env", [lake, "env", sys.executable, "-c", probe], env, source)
    lake_env = json.loads(out["stdout"].strip().splitlines()[-1])
    lean_path = [p for p in (lake_env.get("LEAN_PATH") or "").split(os.pathsep) if p]
    record["lean_path"] = lean_path
    write_json(os.path.join(work, "leanpath.json"), lean_path)

    # What is on disk, by kind of file, under the folders Lean searches.
    kinds = {}
    roots = {}
    listing = []
    inside = os.path.normcase(os.path.abspath(source))
    for root in lean_path:
        if not os.path.isdir(root) or not os.path.normcase(os.path.abspath(root)).startswith(inside):
            continue
        root_total = 0
        for folder, _dirs, names in os.walk(root):
            for name in names:
                path = os.path.join(folder, name)
                size = os.path.getsize(path)
                entry = kinds.setdefault(kind_of(name), {"bytes": 0, "files": 0})
                entry["bytes"] += size
                entry["files"] += 1
                root_total += size
                listing.append((os.path.relpath(path, source).replace(os.sep, "/"), size, path))
        roots[os.path.relpath(root, source).replace(os.sep, "/")] = root_total
    record["disk_by_kind"] = kinds
    record["disk_by_root"] = roots
    record["disk_bytes"] = sum(entry["bytes"] for entry in kinds.values())
    record["disk_files"] = sum(entry["files"] for entry in kinds.values())
    record["source_tree_bytes"] = tree_bytes(source)[0]

    if not options.skip_hash:
        listing.sort()
        started = time.monotonic()
        whole = hashlib.sha256()
        per_kind = {}
        with gzip.open(os.path.join(out_dir, "environment-manifest.tsv.gz"), "wt", encoding="utf-8", newline="\n") as handle:
            for relative, size, path in listing:
                digest = sha256_file(path)
                line = f"{relative}\t{size}\t{digest}\n"
                handle.write(line)
                whole.update(line.encode("utf-8"))
                per_kind.setdefault(kind_of(relative), hashlib.sha256()).update(line.encode("utf-8"))
        record["hash_s"] = round(time.monotonic() - started, 2)
        record["manifest_sha256"] = whole.hexdigest()
        record["manifest_sha256_by_kind"] = {kind: digest.hexdigest() for kind, digest in sorted(per_kind.items())}

    del record["_path"]
    write_json(os.path.join(out_dir, "mathlib.json"), record)
    summary = {key: record[key] for key in ("commit", "download_bytes", "download_files", "disk_bytes", "disk_files")}
    for name in ("clone", "resolve_and_build_tool", "download", "unpack"):
        summary[name + "_s"] = record[name]["wall_s"]
    summary["hash_s"] = record.get("hash_s")
    print(json.dumps(summary, indent=1))
    print(json.dumps(record["disk_by_kind"], indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
