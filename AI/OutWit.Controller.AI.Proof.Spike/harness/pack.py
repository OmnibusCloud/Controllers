"""PF-0 spike: the delivery options of the environment, measured on one runner.

    pack.py --lean-root <toolchain root> --work <dir> --out <results dir> [--levels 3,10]

  * Our own asset with Mathlib's compressor: the .ltar files of the cache as they are (bytes),
    unpacked into an empty folder by the `leantar` the toolchain ships, with no Mathlib
    checkout, no `lake` and no network; the result is compared file by file with what Mathlib's
    cache tool unpacked, and `lean` imports from it.
  * Our own asset with plain zstd: one tar of the files Lean reads, at several levels: bytes,
    seconds to pack, seconds to unpack, parts of at most 2 GiB.
  * A smaller environment: the import closure of some roots (modules, bytes on disk), and what
    Mathlib's cache downloads for each.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time

from pf0lib import EXE, SMALL_PROFILE, lean_environment, run_measured, sha256_file, tree_bytes, write_json

NEEDED = (".olean", ".olean.server", ".olean.private", ".ir")
ROOTS = ["Mathlib", "Mathlib.Tactic", "Mathlib.Basic.Real.Basic", "Mathlib.Data.Nat.Prime.Basic",
         "Mathlib.Algebra.BigOperators.Group.Finset.Basic", "Mathlib.Analysis.SpecialFunctions.Pow.Real",
         "Mathlib.Analysis.SpecialFunctions.Trigonometric.Basic", "Mathlib.NumberTheory.ArithmeticFunction.Defs",
         "Mathlib.Topology.Basic", "Mathlib.Basic.Complex.Basic", "Mathlib.LinearAlgebra.Matrix.Determinant.Basic"]
IMPORT = re.compile(r"^(?:public\s+|private\s+)?(?:meta\s+)?import\s+(?:all\s+)?(\S+)")
TWO_GIB = 1 << 31


def kind_of(name: str) -> str | None:
    for suffix in (".olean.server", ".olean.private", ".olean", ".ir"):
        if name.endswith(suffix):
            return suffix
    return None


def needed_files(root: str) -> list:
    """The compiled files Lean reads, under the library folders (a `lake` configuration compiled
    beside them is not one of them)."""
    found = []
    for folder, _dirs, names in os.walk(root):
        for name in names:
            if kind_of(name):
                path = os.path.join(folder, name)
                relative = os.path.relpath(path, root).replace(os.sep, "/")
                if "/build/lib/lean/" in "/" + relative:
                    found.append((relative, path))
    found.sort()
    return found


def digest_by_kind(files: list, skip_prefix: str = "") -> dict:
    digests = {}
    for relative, path in files:
        if skip_prefix and relative.startswith(skip_prefix):
            continue
        line = f"{relative}\t{os.path.getsize(path)}\t{sha256_file(path)}\n"
        digests.setdefault(kind_of(relative), hashlib.sha256()).update(line.encode("utf-8"))
    return {kind: value.hexdigest() for kind, value in sorted(digests.items())}


def imports_of(path: str) -> list:
    """The modules a source file imports (its header only)."""
    found = []
    depth = 0
    try:
        with open(path, encoding="utf-8", errors="replace") as handle:
            for raw in handle:
                line = raw.strip()
                if depth:
                    depth += line.count("/-") - line.count("-/")
                    continue
                if not line or line.startswith("--") or line.split()[0] in ("module", "prelude"):
                    continue
                if line.startswith("/-"):
                    depth = line.count("/-") - line.count("-/")
                    continue
                match = IMPORT.match(line)
                if not match:
                    break
                found.append(match.group(1))
    except OSError:
        pass
    return found


def closures(source: str, lean_path: list) -> list:
    """For each root: the modules of its import closure and their bytes on disk.

    Only the environment's own folders count: the search path also ends with the toolchain's
    library, and the modules of Lean itself that Mathlib imports are not part of the environment.
    """
    sizes = {}
    needed = {}
    inside = os.path.normcase(os.path.abspath(source))
    for root in lean_path:
        if not os.path.normcase(os.path.abspath(root)).startswith(inside):
            continue
        for folder, _dirs, names in os.walk(root):
            for name in names:
                stem = name.split(".", 1)[0]
                module = os.path.relpath(os.path.join(folder, stem), root).replace(os.sep, ".")
                size = os.path.getsize(os.path.join(folder, name))
                sizes[module] = sizes.get(module, 0) + size
                if kind_of(name):
                    needed[module] = needed.get(module, 0) + size
    sources = {}
    roots = [source] + [os.path.join(source, ".lake", "packages", name)
                        for name in sorted(os.listdir(os.path.join(source, ".lake", "packages")))]
    for root in roots:
        for folder, dirs, names in os.walk(root):
            dirs[:] = [d for d in dirs if d not in (".lake", ".git")]
            for name in names:
                if name.endswith(".lean"):
                    module = os.path.relpath(os.path.join(folder, name[:-5]), root).replace(os.sep, ".")
                    if module in sizes:
                        sources[module] = os.path.join(folder, name)
    graph = {module: [name for name in imports_of(path) if name in sizes] for module, path in sources.items()}
    out = []
    for roots in [[root] for root in ROOTS] + [list(SMALL_PROFILE)]:
        if any(root not in graph for root in roots):
            continue
        seen = set(roots)
        stack = list(roots)
        while stack:
            for name in graph.get(stack.pop(), []):
                if name not in seen:
                    seen.add(name)
                    stack.append(name)
        label = roots[0] if len(roots) == 1 else "the smaller profile: " + ", ".join(roots)
        out.append({"root": label, "roots": roots, "modules": len(seen), "bytes": sum(sizes[m] for m in seen),
                    "needed_bytes": sum(needed.get(m, 0) for m in seen)})
    return out


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lean-root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--levels", default="3,10")
    options = parser.parse_args()
    work = os.path.abspath(options.work)
    bin_dir = os.path.join(os.path.abspath(options.lean_root), "bin")
    source = os.path.join(work, "mathlib4")
    cache = os.path.join(work, "mathlib-cache")
    with open(os.path.join(work, "leanpath.json"), encoding="utf-8") as handle:
        lean_path = json.load(handle)
    record = {"variants": []}

    # 1. The cache's own files as our asset, unpacked by the toolchain's leantar alone.
    ltars = []
    for folder, _dirs, names in os.walk(cache):
        ltars += [os.path.join(folder, name) for name in names if name.endswith(".ltar")]
    ltars.sort()
    ltar_bytes = sum(os.path.getsize(path) for path in ltars)
    target = os.path.join(work, "asset-unpack")
    shutil.rmtree(target, ignore_errors=True)
    os.makedirs(target)
    out = run_measured([os.path.join(bin_dir, "leantar" + EXE), "-x", "-j", "-"], cwd=target,
                       stdin_text=json.dumps(ltars), timeout=3600)
    entry = {"name": "Mathlib's compressor: the cache's .ltar files, unpacked by the toolchain's leantar",
             "bytes": ltar_bytes, "files": len(ltars), "seconds": None, "parts_2gib": math.ceil(ltar_bytes / TWO_GIB),
             "unpack_s": out["wall_s"], "unpack_exit": out["exit"], "unpack_cpu_s": round(out["user_s"] + out["sys_s"], 2)}
    if out["exit"] != 0:
        entry["error"] = (out["stdout"] + out["stderr"])[-400:]
    else:
        entry["unpacked_bytes"] = tree_bytes(target)[0]
        started = time.monotonic()
        ours = digest_by_kind(needed_files(target))
        entry["hash_s"] = round(time.monotonic() - started, 2)
        # Mathlib's own tool also builds its cache program there (module Cache): not part of the asset.
        theirs = digest_by_kind(needed_files(source), skip_prefix=".lake/build/lib/lean/Cache")
        entry["same_files_as_the_cache_tool"] = ours == theirs
        entry["digests"] = {kind: value[:16] for kind, value in ours.items()}
        asset_path = [os.path.join(target, os.path.relpath(p, source)) for p in lean_path]
        probe = os.path.join(target, "probe")
        os.makedirs(probe)
        with open(os.path.join(probe, "Probe.lean"), "w", encoding="utf-8", newline="\n") as handle:
            handle.write("import Mathlib\nexample (x : Real) (h : 2 * x + 3 = 7) : x = 2 := by linarith\n")
        probe_out = run_measured([os.path.join(bin_dir, "lean" + EXE), "Probe.lean"],
                                 env=lean_environment(asset_path, bin_dir=bin_dir), cwd=probe, timeout=900)
        entry["lean_imports_from_it"] = probe_out["exit"] == 0
        entry["probe_s"] = probe_out["wall_s"]
        if probe_out["exit"] != 0:
            entry["probe_says"] = (probe_out["stdout"] + probe_out["stderr"])[-400:]
    record["variants"].append(entry)
    print(json.dumps(entry), flush=True)

    # 2. Plain zstd over one tar of the files Lean reads.
    files = needed_files(source)
    files = [(relative, path) for relative, path in files if not relative.startswith(".lake/build/lib/lean/Cache")]
    listing = os.path.join(work, "pack-files.txt")
    with open(listing, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(relative for relative, _path in files) + "\n")
    raw_bytes = sum(os.path.getsize(path) for _relative, path in files)
    record["needed_bytes"] = raw_bytes
    record["needed_files"] = len(files)
    for level in [int(part) for part in options.levels.split(",") if part]:
        archive = os.path.join(work, f"environment-{level}.tar.zst")
        flags = [f"-{level}", "-T0"] + (["--long=27"] if level >= 19 else [])
        started = time.monotonic()
        tar = subprocess.Popen(["tar", "-cf", "-", "-C", source, "-T", listing], stdout=subprocess.PIPE)
        zstd = subprocess.run(["zstd", "-q", "-f"] + flags + ["-o", archive], stdin=tar.stdout)
        tar.stdout.close()
        tar.wait()
        seconds = round(time.monotonic() - started, 1)
        entry = {"name": f"plain zstd -{level}" + (" --long=27" if level >= 19 else "") + ", one tar",
                 "bytes": os.path.getsize(archive) if zstd.returncode == 0 else 0, "seconds": seconds,
                 "raw_bytes": raw_bytes}
        entry["parts_2gib"] = math.ceil(entry["bytes"] / TWO_GIB)
        unpack = os.path.join(work, "zstd-unpack")
        shutil.rmtree(unpack, ignore_errors=True)
        os.makedirs(unpack)
        started = time.monotonic()
        zstd = subprocess.Popen(["zstd", "-dc", "--long=27", archive], stdout=subprocess.PIPE)
        subprocess.run(["tar", "-xf", "-", "-C", unpack], stdin=zstd.stdout)
        zstd.stdout.close()
        zstd.wait()
        entry["unpack_s"] = round(time.monotonic() - started, 1)
        shutil.rmtree(unpack, ignore_errors=True)
        os.remove(archive)
        record["variants"].append(entry)
        print(json.dumps(entry), flush=True)

    # 3. Smaller environments.
    record["closures"] = closures(source, lean_path)
    lake = os.path.join(bin_dir, "lake" + EXE)
    for entry in record["closures"]:
        folder = os.path.join(work, "closure-cache")
        shutil.rmtree(folder, ignore_errors=True)
        env = dict(os.environ)
        env["PATH"] = bin_dir + os.pathsep + env.get("PATH", "")
        env["MATHLIB_CACHE_DIR"] = folder
        out = run_measured([lake, "exe", "cache", "get-"] + entry["roots"], env=env, cwd=source, timeout=1800)
        if out["exit"] == 0:
            size = 0
            count = 0
            for path_folder, _dirs, names in os.walk(folder):
                for name in names:
                    if name.endswith(".ltar"):
                        size += os.path.getsize(os.path.join(path_folder, name))
                        count += 1
            entry["download_bytes"] = size
            entry["download_files"] = count
            entry["download_s"] = out["wall_s"]
        print(json.dumps(entry), flush=True)
    shutil.rmtree(os.path.join(work, "closure-cache"), ignore_errors=True)
    shutil.rmtree(target, ignore_errors=True)
    write_json(os.path.join(options.out, "pack.json"), record)
    return 0


if __name__ == "__main__":
    sys.exit(main())
