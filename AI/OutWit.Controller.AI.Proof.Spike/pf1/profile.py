"""PF-1 spike: `replay` profiles per Mathlib revision (spec section 3.2).

    profile.py fetch   --rev <commit> --root <lean root> --work <dir> --out <dir>
    profile.py https   --manifest <ltar.tsv> --root <lean root> --work <dir> --out <dir>
    profile.py compare <dir> <dir> [<dir>]
    profile.py old     --which A|B|C --work <dir> --out <dir> --pin-root <pinned lean root>
    profile.py rebuild --rev <commit> --root <lean root> --work <dir> --out <dir> --minutes 300

`fetch` is Mathlib's own route: a checkout of the revision, `lake exe cache get-` and `unpack`; it
writes a manifest of the cache's `.ltar` files and one of every file Lean reads (path, size,
SHA-256), and the digest of each kind of file as PF-0 computed them. `https` is the route without
Mathlib's tool, `git` or `lake`: every `.ltar` the first manifest names fetched by its key over plain
HTTPS from the cache's public endpoint, checked against that manifest, unpacked by the toolchain's
`leantar`, hashed the same way. `compare` says whether the manifests of different runners agree.
`old` asks whether the cache still serves an older revision, with that revision's toolchain and
tool, and (for A and B) exports a small file through the separate `lean4export` at its matching tag
and reads the export with the pinned toolchain's kernels. `rebuild` builds the revision from source
for as long as a hosted runner allows and compares every file it built with the cache's bytes.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import gzip
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.request

from common import (EXE, capped, comparator_command, comparator_config, export_targets, fetch_lean, kernel_command,
                    lean_env, load_pins, read_export_summary, run_measured, sha256_file, tool, tree_bytes, write_json)

KINDS = (".olean.server", ".olean.private", ".olean", ".ilean", ".ir", ".c", ".hash", ".trace")


def kind_of(name: str) -> str:
    for suffix in KINDS:
        if name.endswith(suffix):
            return suffix
    return os.path.splitext(name)[1] or "(none)"


def clone(repo: str, commit: str, dest: str) -> dict:
    if os.path.isdir(dest):
        shutil.rmtree(dest)
    os.makedirs(dest)
    started = time.monotonic()
    for args in (["init", "-q"], ["remote", "add", "origin", repo], ["fetch", "-q", "--depth", "1", "origin", commit],
                 ["checkout", "-q", "FETCH_HEAD"]):
        subprocess.run(["git"] + args, cwd=dest, check=True)
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=dest, capture_output=True, text=True).stdout.strip()
    return {"head": head, "clone_s": round(time.monotonic() - started, 1), "clone_bytes": tree_bytes(dest)[0]}


def short(row: dict, lines: int = 15) -> dict:
    out = {k: row.get(k) for k in ("exit", "wall_s", "user_s", "sys_s", "peak_rss", "killed")}
    text = ((row.get("stdout") or "") + "\n" + (row.get("stderr") or "")).strip().splitlines()
    out["tail"] = [line[:300] for line in text[-lines:]]
    return out


def lake_env_path(root: str, source: str, env: dict) -> list:
    probe = "import json, os; print(json.dumps(os.environ.get('LEAN_PATH', '')))"
    row = run_measured([tool(root, "lake"), "env", sys.executable, "-c", probe], env=env, cwd=source, timeout=600)
    try:
        value = json.loads(row["stdout"].strip().splitlines()[-1])
    except (ValueError, IndexError):
        return []
    return [p for p in value.split(os.pathsep) if p]


def manifest_files(source: str, lean_path: list, out_path: str) -> dict:
    """What PF-0's mathlib.py hashed: every file under the search path's folders inside the
    checkout, by its path relative to the checkout; one digest per kind of file."""
    listing = []
    inside = os.path.normcase(os.path.abspath(source))
    for root in lean_path:
        if not os.path.isdir(root) or not os.path.normcase(os.path.abspath(root)).startswith(inside):
            continue
        for folder, _dirs, names in os.walk(root):
            for name in names:
                path = os.path.join(folder, name)
                listing.append((os.path.relpath(path, source).replace(os.sep, "/"), os.path.getsize(path), path))
    return write_manifest(listing, out_path)


def write_manifest(listing: list, out_path: str) -> dict:
    listing.sort()
    started = time.monotonic()
    whole = hashlib.sha256()
    per_kind = {}
    counts = {}
    total = 0
    with gzip.open(out_path, "wt", encoding="utf-8", newline="\n") as handle:
        for relative, size, path in listing:
            line = f"{relative}\t{size}\t{sha256_file(path)}\n"
            handle.write(line)
            whole.update(line.encode("utf-8"))
            kind = kind_of(relative)
            per_kind.setdefault(kind, hashlib.sha256()).update(line.encode("utf-8"))
            counts[kind] = counts.get(kind, 0) + 1
            total += size
    return {"files": len(listing), "bytes": total, "sha256": whole.hexdigest(), "hash_s": round(time.monotonic() - started, 1),
            "by_kind": {k: {"sha256": v.hexdigest(), "files": counts[k]} for k, v in sorted(per_kind.items())}}


def ltar_manifest(cache: str, out_path: str) -> dict:
    listing = []
    for folder, _dirs, names in os.walk(cache):
        for name in names:
            if name.endswith(".ltar"):
                path = os.path.join(folder, name)
                listing.append((name, os.path.getsize(path), path))
    listing.sort()
    whole = hashlib.sha256()
    total = 0
    with open(out_path, "w", encoding="utf-8", newline="\n") as handle:
        for name, size, path in listing:
            line = f"{name}\t{size}\t{sha256_file(path)}\n"
            handle.write(line)
            whole.update(line.encode("utf-8"))
            total += size
    return {"files": len(listing), "bytes": total, "sha256": whole.hexdigest()}


def fetch(options: argparse.Namespace) -> int:
    pins = load_pins()
    root = os.path.abspath(options.root)
    work = os.path.abspath(options.work)
    out = os.path.abspath(options.out)
    os.makedirs(out, exist_ok=True)
    source = os.path.join(work, "mathlib4")
    cache = os.path.join(work, "mathlib-cache")
    env = lean_env(root, extra={"MATHLIB_CACHE_DIR": cache, "GIT_TERMINAL_PROMPT": "0"})
    record = {"rev": options.rev}
    record.update(clone(pins["MATHLIB_REPO"], options.rev, source))
    with open(os.path.join(source, "lean-toolchain"), encoding="utf-8") as handle:
        record["lean_toolchain"] = handle.read().strip()
    for name, args in (("tool", ["exe", "cache"]), ("download", ["exe", "cache", "get-"]), ("unpack", ["exe", "cache", "unpack"])):
        print(f"--- lake {' '.join(args)}", flush=True)
        row = run_measured([tool(root, "lake")] + args, env=env, cwd=source, timeout=3600)
        record[name] = short(row)
        print("\n".join(record[name]["tail"]), flush=True)
        if row["exit"] != 0 and name != "tool":
            write_json(os.path.join(out, "fetch.json"), record)
            return 1
    record["ltar"] = ltar_manifest(cache, os.path.join(out, "ltar.tsv"))
    lean_path = lake_env_path(root, source, env)
    record["lean_path"] = [os.path.relpath(p, source).replace(os.sep, "/") for p in lean_path]
    record["files"] = manifest_files(source, lean_path, os.path.join(out, "files.tsv.gz"))
    # The environment Lean reads, used: one file importing all of Mathlib.
    probe = os.path.join(work, "probe")
    os.makedirs(probe, exist_ok=True)
    with open(os.path.join(probe, "Probe.lean"), "w", encoding="utf-8") as handle:
        handle.write("import Mathlib\nexample : (2 : ℝ) + 2 = 4 := by norm_num\n")
    row = run_measured([tool(root, "lean"), "Probe.lean"], env=lean_env(root, lean_path), cwd=probe, timeout=1200)
    record["probe"] = short(row)
    write_json(os.path.join(out, "fetch.json"), record)
    print(json.dumps({k: record[k] for k in ("ltar", "files")}, indent=1))
    return 0


def https(options: argparse.Namespace) -> int:
    pins = load_pins()
    root = os.path.abspath(options.root)
    work = os.path.abspath(options.work)
    out = os.path.abspath(options.out)
    os.makedirs(out, exist_ok=True)
    base = options.base_url or pins["CACHE_FILES_URL"]
    wanted = []
    with open(options.manifest, encoding="utf-8") as handle:
        for line in handle:
            name, size, digest = line.rstrip("\n").split("\t")
            wanted.append((name, int(size), digest))
    target = os.path.join(work, "ltar")
    os.makedirs(target, exist_ok=True)

    def get(entry):
        name, size, digest = entry
        path = os.path.join(target, name)
        for attempt in range(4):
            try:
                request = urllib.request.Request(f"{base}/{name}", headers={"User-Agent": "pf1-spike"})
                with urllib.request.urlopen(request, timeout=120) as response, open(path, "wb") as writer:
                    status = response.status
                    shutil.copyfileobj(response, writer, 1 << 20)
                return name, status, sha256_file(path) == digest, os.path.getsize(path)
            except Exception as error:  # noqa: BLE001
                last = str(error)
                time.sleep(2 * (attempt + 1))
        return name, last, False, 0

    started = time.monotonic()
    results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=16) as pool:
        for result in pool.map(get, wanted):
            results.append(result)
    record = {"base_url": base, "wanted": len(wanted), "download_s": round(time.monotonic() - started, 1),
              "fetched": sum(1 for r in results if r[1] == 200), "same_bytes": sum(1 for r in results if r[2]),
              "bytes": sum(r[3] for r in results),
              "failures": [{"name": r[0], "status": r[1]} for r in results if not r[2]][:20]}
    # Unpacked by the toolchain's leantar into an empty folder: no checkout, no lake, no git.
    unpacked = os.path.join(work, "unpacked")
    shutil.rmtree(unpacked, ignore_errors=True)
    os.makedirs(unpacked)
    paths = sorted(os.path.join(target, r[0]) for r in results if r[2])
    row = run_measured([tool(root, "leantar"), "-x", "-j", "-"], cwd=unpacked, stdin_text=json.dumps(paths), timeout=3600)
    record["unpack"] = short(row)
    listing = []
    for folder, _dirs, names in os.walk(unpacked):
        for name in names:
            path = os.path.join(folder, name)
            relative = os.path.relpath(path, unpacked).replace(os.sep, "/")
            if "/build/lib/lean/" in "/" + relative:
                listing.append((relative, os.path.getsize(path), path))
    record["files"] = write_manifest(listing, os.path.join(out, "files-https.tsv.gz"))
    write_json(os.path.join(out, "https.json"), record)
    print(json.dumps({k: v for k, v in record.items() if k != "failures"}, indent=1))
    return 0


def read_manifest(path: str) -> dict:
    opener = gzip.open if path.endswith(".gz") else open
    entries = {}
    with opener(path, "rt", encoding="utf-8") as handle:
        for line in handle:
            name, size, digest = line.rstrip("\n").split("\t")
            entries[name] = (int(size), digest)
    return entries


def compare(options: argparse.Namespace) -> int:
    """Manifests of different runners side by side: the cache's files, and the files Lean reads
    (only the kinds Lean reads; build traces written on the spot may differ)."""
    record = {}
    lean_kinds = (".olean", ".olean.server", ".olean.private", ".ir")
    sets = {}
    for folder in options.dirs:
        label = os.path.basename(os.path.normpath(folder))
        entry = {}
        for name in ("ltar.tsv", "files.tsv.gz", "files-https.tsv.gz"):
            path = os.path.join(folder, name)
            if os.path.isfile(path):
                manifest = read_manifest(path)
                if name.startswith("files"):
                    manifest = {k: v for k, v in manifest.items() if kind_of(k) in lean_kinds
                                and not k.startswith(".lake/build/lib/lean/Cache")}
                    # The https route unpacks into an empty folder: paths are the same relative ones.
                entry[name] = manifest
        sets[label] = entry
    labels = list(sets)
    for i, a in enumerate(labels):
        for b in labels[i + 1:]:
            for left in sets[a]:
                for right in sets[b]:
                    if left.split("-")[0].split(".")[0] != right.split("-")[0].split(".")[0]:
                        continue
                    x, y = sets[a][left], sets[b][right]
                    same = sum(1 for k in x if k in y and x[k] == y[k])
                    record[f"{a}/{left} vs {b}/{right}"] = {
                        "left": len(x), "right": len(y), "same": same,
                        "only_left": len(set(x) - set(y)), "only_right": len(set(y) - set(x)),
                        "different": sum(1 for k in x if k in y and x[k] != y[k]),
                        "examples": [k for k in x if k in y and x[k] != y[k]][:5]}
    write_json(options.out, record)
    print(json.dumps(record, indent=1))
    return 0


OLD_FILE = """import Mathlib.Data.Nat.Prime.Basic

theorem pf1_old (n : ℕ) (h : 2 ≤ n) : ∃ p, Nat.Prime p ∧ p ∣ n :=
  ⟨n.minFac, Nat.minFac_prime (by omega), Nat.minFac_dvd n⟩
"""
OLD_CHALLENGE = """import Mathlib.Data.Nat.Prime.Basic

theorem pf1_old (n : ℕ) (h : 2 ≤ n) : ∃ p, Nat.Prime p ∧ p ∣ n := sorry
"""
CORE_FILE = """theorem pf1_old (n m : Nat) : n + m = m + n := Nat.add_comm n m
"""
CORE_CHALLENGE = """theorem pf1_old (n m : Nat) : n + m = m + n := sorry
"""


def old(options: argparse.Namespace) -> int:
    pins = load_pins()
    which = options.which
    name, commit, version = pins[f"OLD_{which}_NAME"], pins[f"OLD_{which}_COMMIT"], pins[f"OLD_{which}_LEAN"]
    work = os.path.abspath(os.path.join(options.work, f"old-{which}"))
    out = os.path.abspath(options.out)
    os.makedirs(work, exist_ok=True)
    os.makedirs(out, exist_ok=True)
    record = {"name": name, "commit": commit, "lean": version}
    path = os.path.join(out, f"old-{which}.json")
    toolchain = fetch_lean(version, "linux-x64", os.path.join(work, "tc"))
    record["toolchain"] = {k: toolchain[k] for k in ("asset", "sha256", "checked", "bytes")}
    root = toolchain["root"]
    source = os.path.join(work, "mathlib4")
    record.update(clone(pins["MATHLIB_REPO"], commit, source))
    cache = os.path.join(work, "mathlib-cache")
    env = lean_env(root, extra={"MATHLIB_CACHE_DIR": cache, "GIT_TERMINAL_PROMPT": "0"})
    sources = sum(1 for _ in _lean_files(os.path.join(source, "Mathlib")))
    record["mathlib_modules"] = sources

    def attempt(label: str, extra: dict) -> None:
        print(f"--- {label}: lake exe cache get", flush=True)
        row = run_measured([tool(root, "lake"), "exe", "cache", "get"], env=dict(env, **extra), cwd=source, timeout=2700)
        entry = short(row, 25)
        entry["ltar_files"], entry["ltar_bytes"] = (_count(cache, ".ltar"))
        # An older tool keeps its files under the home folder whatever MATHLIB_CACHE_DIR says.
        home_cache = os.path.join(os.path.expanduser("~"), ".cache", "mathlib")
        entry["ltar_files_home"], entry["ltar_bytes_home"] = (_count(home_cache, ".ltar"))
        # Older Lake versions put the compiled files under lib/, newer ones under lib/lean/.
        entry["oleans"] = _count_modules(os.path.join(source, ".lake", "build", "lib"))
        text = (row.get("stdout") or "") + (row.get("stderr") or "")
        said = re.findall(r"Downloaded: (\d+) file\(s\) \[attempted (\d+)/(\d+)", text)
        if said:
            entry["tool_says_downloaded"] = [int(x) for x in said[-1]]
        entry["urls"] = sorted(set(re.findall(r"https?://[A-Za-z0-9.\-]+(?:/[A-Za-z0-9.\-_]+){0,2}", text)))[:10]
        record[label] = entry
        print("\n".join(entry["tail"]), flush=True)
        write_json(path, record)

    attempt("tool", {})
    if record["tool"]["oleans"] < sources:
        # The same keys from today's endpoint, through the old tool's own override.
        attempt("tool_today_endpoint", {"MATHLIB_CACHE_GET_URL": "https://cache.mathlib.org/mathlib4-master"})
    served = max(record.get("tool", {}).get("oleans", 0), record.get("tool_today_endpoint", {}).get("oleans", 0))
    record["served_fraction"] = round(served / sources, 4) if sources else None
    write_json(path, record)
    if which not in ("A", "B"):
        return 0

    # The separate exporter at the matching tag, built with that toolchain.
    tag, tag_commit = pins[f"LEAN4EXPORT_{which}_TAG"], pins[f"LEAN4EXPORT_{which}_COMMIT"]
    exporter_src = os.path.join(work, "lean4export")
    record["lean4export"] = {"tag": tag}
    record["lean4export"].update(clone(pins["LEAN4EXPORT_REPO"], tag_commit, exporter_src))
    row = run_measured([tool(root, "lake"), "build"], env=lean_env(root), cwd=exporter_src, timeout=1800)
    record["lean4export"]["build"] = short(row)
    exporter = os.path.join(exporter_src, ".lake", "build", "bin", "lean4export" + EXE)
    if not os.path.isfile(exporter):
        write_json(path, record)
        return 1
    with_mathlib = served == sources
    lean_path = lake_env_path(root, source, env) if with_mathlib else []
    files = os.path.join(work, "files")
    os.makedirs(files, exist_ok=True)
    text, challenge = (OLD_FILE, OLD_CHALLENGE) if with_mathlib else (CORE_FILE, CORE_CHALLENGE)
    record["export_with_mathlib"] = with_mathlib
    exports = {}
    # The names an export carries for the pinned comparator, as far as this Lean has them.
    wanted = export_targets(["pf1_old"])
    header = "import Mathlib.Data.Nat.Prime.Basic" if with_mathlib else "import Lean"
    present = present_names(root, lean_path, files, header, [n for n in wanted if n != "pf1_old"])
    record["names_missing_in_this_lean"] = [n for n, ok in present.items() if not ok]
    targets = ["pf1_old"] + [n for n in wanted if present.get(n)]
    for module, body in (("PF1Old", text), ("PF1OldChallenge", challenge)):
        with open(os.path.join(files, module + ".lean"), "w", encoding="utf-8", newline="\n") as handle:
            handle.write(body)
        fenv = lean_env(root, lean_path + [files])
        built = run_measured([tool(root, "lean"), "-o", module + ".olean", module + ".lean"], env=fenv, cwd=files,
                             timeout=1800)
        record[f"compile_{module}"] = short(built)
        target = os.path.join(files, module + ".ndjson")
        row = run_measured([exporter, module, "--"] + targets, env=fenv, cwd=files, timeout=1800, stdout_path=target)
        record[f"export_{module}"] = short(row)
        record[f"export_{module}"]["head"] = ((row.get("stderr") or "") + (row.get("stdout") or "")).strip().splitlines()[:6]
        if row["exit"] == 0:
            record[f"export_{module}"].update(read_export_summary(target))
            exports[module] = target
    write_json(path, record)
    if len(exports) < 2:
        return 1
    # Read by the pinned toolchain's kernels.
    pin = os.path.abspath(options.pin_root)
    readers = {}
    for kernel in ("leanchecker", "nanoda-4", "con-ron", "con-leche"):
        row = capped(kernel_command(pin, kernel, exports["PF1Old"], files), env=lean_env(pin, []), cwd=files, timeout=1800)
        row.pop("json", None)
        readers[kernel] = row
    config = comparator_config(os.path.join(files, "comparator.json"), ["pf1_old"])
    row = capped(comparator_command(pin, config, exports["PF1OldChallenge"], exports["PF1Old"], sandbox=False),
                 env=lean_env(pin, []), cwd=files, timeout=1800)
    row.pop("json", None)
    readers["comparator"] = row
    record["pinned_readers"] = readers
    write_json(path, record)
    for kernel, row in readers.items():
        print(kernel, row["exit"], row["wall_s"], row["says"][-200:])
    return 0


def _lean_files(folder: str):
    for base, _dirs, names in os.walk(folder):
        for name in names:
            if name.endswith(".lean"):
                yield os.path.join(base, name)


def _count_modules(folder: str) -> int:
    count = 0
    if os.path.isdir(folder):
        for base, _dirs, names in os.walk(folder):
            if "Mathlib" not in base.replace(os.sep, "/").split("/"):
                continue
            count += sum(1 for name in names if name.endswith(".olean"))
    return count


def present_names(root: str, lean_path: list, folder: str, header: str, names: list) -> dict:
    """Which of `names` the environment of that toolchain (and header) has."""
    lines = [header, "open Lean Elab Command in", "#eval show CommandElabM Unit from do",
             "  let env ← getEnv"]
    for name in names:
        lines.append(f"  IO.println s!\"PF1NAME {name} {{env.contains `{name}}}\"")
    path = os.path.join(folder, "PF1Probe.lean")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")
    row = run_measured([tool(root, "lean"), "PF1Probe.lean"], env=lean_env(root, lean_path + [folder]), cwd=folder,
                       timeout=900)
    found = {}
    for line in (row.get("stdout") or "").splitlines():
        parts = line.strip().split()
        if len(parts) == 3 and parts[0] == "PF1NAME":
            found[parts[1]] = parts[2] == "true"
    return found


def _count(folder: str, suffix: str) -> tuple:
    files = 0
    total = 0
    if os.path.isdir(folder):
        for base, _dirs, names in os.walk(folder):
            for name in names:
                if name.endswith(suffix):
                    files += 1
                    total += os.path.getsize(os.path.join(base, name))
    return files, total


def rebuild(options: argparse.Namespace) -> int:
    """The revision built from source for as long as the runner allows, every built file compared
    with the cache's bytes for the same path."""
    root = os.path.abspath(options.root)
    work = os.path.abspath(options.work)
    out = os.path.abspath(options.out)
    source = os.path.join(work, "mathlib4")
    record = {"rev": options.rev, "minutes": options.minutes}
    path = os.path.join(out, "rebuild.json")
    cache_manifest = read_manifest(os.path.join(out, "files.tsv.gz"))
    record["cache_files"] = len(cache_manifest)
    # Everything a build writes goes: the root's and every package's build folder.
    removed = 0
    for folder in [os.path.join(source, ".lake", "build")] + [
            os.path.join(source, ".lake", "packages", p, ".lake", "build")
            for p in os.listdir(os.path.join(source, ".lake", "packages"))]:
        if os.path.isdir(folder):
            removed += tree_bytes(folder)[0]
            shutil.rmtree(folder)
    record["removed_bytes"] = removed
    env = lean_env(root, extra={"GIT_TERMINAL_PROMPT": "0", "MATHLIB_CACHE_DIR": os.path.join(work, "empty-cache")})
    print("--- lake build Mathlib (from source)", flush=True)
    row = run_measured([tool(root, "lake"), "build", "Mathlib"], env=env, cwd=source, timeout=options.minutes * 60,
                       keep=1 << 20)
    record["build"] = short(row, 30)
    built = {}
    for folder, _dirs, names in os.walk(source):
        if "/build/lib/lean" not in folder.replace(os.sep, "/") + "/":
            continue
        for name in names:
            full = os.path.join(folder, name)
            relative = os.path.relpath(full, source).replace(os.sep, "/")
            if kind_of(name) in (".olean", ".olean.server", ".olean.private", ".ilean", ".ir", ".c"):
                built[relative] = full
    by_kind = {}
    examples = []
    for relative, full in sorted(built.items()):
        kind = kind_of(relative)
        entry = by_kind.setdefault(kind, {"built": 0, "in_cache": 0, "same": 0, "different": 0})
        entry["built"] += 1
        theirs = cache_manifest.get(relative)
        if theirs is None:
            continue
        entry["in_cache"] += 1
        if theirs == (os.path.getsize(full), sha256_file(full)):
            entry["same"] += 1
        else:
            entry["different"] += 1
            if len(examples) < 10:
                examples.append({"file": relative, "cache_bytes": theirs[0], "built_bytes": os.path.getsize(full)})
    modules = sum(1 for r in built if r.endswith(".olean") and "/Mathlib/" in r)
    record["built_by_kind"] = by_kind
    record["mathlib_modules_built"] = modules
    record["different_examples"] = examples
    write_json(path, record)
    print(json.dumps(record, indent=1))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("step", choices=["fetch", "https", "compare", "old", "rebuild"])
    parser.add_argument("dirs", nargs="*")
    parser.add_argument("--rev", default="")
    parser.add_argument("--root", default="")
    parser.add_argument("--pin-root", default="")
    parser.add_argument("--work", default="")
    parser.add_argument("--out", default="")
    parser.add_argument("--manifest", default="")
    parser.add_argument("--base-url", default="")
    parser.add_argument("--which", default="")
    parser.add_argument("--minutes", type=float, default=300)
    options = parser.parse_args()
    return {"fetch": fetch, "https": https, "compare": compare, "old": old, "rebuild": rebuild}[options.step](options)


if __name__ == "__main__":
    sys.exit(main())
