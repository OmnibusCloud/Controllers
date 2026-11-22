"""PF-0 spike: the official Lean release of one platform, fetched, verified and unpacked.

    toolchain.py <rid> <dest-dir> <record.json>

Writes a record (bytes downloaded, the digest and the time to check it, the time to unpack,
bytes and files on disk, the programs in bin/, the licence files) and, under GitHub Actions,
exports the toolchain's root folder as LEAN_ROOT.
"""
from __future__ import annotations

import os
import subprocess
import sys
import time

from pf0lib import EXE, load_pins, rid_key, run_measured, sha256_file, tree_bytes, write_json

HERE = os.path.dirname(os.path.abspath(__file__))


def unpack(archive: str, dest: str) -> str:
    """Unpacks a .tar.zst and says how: Python's own reader when it has one, else zstd and tar."""
    os.makedirs(dest, exist_ok=True)
    if sys.version_info >= (3, 14):
        import tarfile
        with tarfile.open(archive, "r:zst") as tar:
            tar.extractall(dest, filter="fully_trusted")
        return "python tarfile (zstd)"
    zstd = subprocess.Popen(["zstd", "-dc", archive], stdout=subprocess.PIPE)
    tar = subprocess.run(["tar", "-xf", "-", "-C", dest], stdin=zstd.stdout)
    zstd.stdout.close()
    if zstd.wait() != 0 or tar.returncode != 0:
        raise RuntimeError("zstd | tar failed")
    return "zstd -dc | tar -xf -"


def main() -> int:
    rid, dest, record_path = sys.argv[1], os.path.abspath(sys.argv[2]), sys.argv[3]
    pins = load_pins(os.path.join(HERE, "..", "pins.env"))
    key = rid_key(rid)
    name = pins[f"LEAN_{key}_FILE"]
    url = pins["LEAN_URL_PREFIX"] + "/" + name
    os.makedirs(dest, exist_ok=True)
    archive = os.path.join(dest, name)
    record = {"rid": rid, "lean_version": pins["LEAN_VERSION"], "url": url,
              "pinned_sha256": pins[f"LEAN_{key}_SHA256"]}

    started = time.monotonic()
    subprocess.run(["curl", "-fsSL", "--retry", "3", "--max-time", "1800", "-o", archive, url], check=True)
    record["download_s"] = round(time.monotonic() - started, 2)
    record["archive_bytes"] = os.path.getsize(archive)

    started = time.monotonic()
    seen = sha256_file(archive)
    record["sha256_s"] = round(time.monotonic() - started, 2)
    record["sha256"] = seen
    if seen != record["pinned_sha256"]:
        os.remove(archive)
        print(f"HASH MISMATCH {name}: expected {record['pinned_sha256']}, got {seen}", file=sys.stderr)
        return 1
    print(f"pinned   {seen}  {name}", file=sys.stderr)

    started = time.monotonic()
    record["unpacked_with"] = unpack(archive, os.path.join(dest, "lean"))
    record["unpack_s"] = round(time.monotonic() - started, 2)
    os.remove(archive)

    tops = os.listdir(os.path.join(dest, "lean"))
    root = os.path.join(dest, "lean", tops[0]) if len(tops) == 1 else os.path.join(dest, "lean")
    total, count = tree_bytes(root)
    record["root"] = root
    record["unpacked_bytes"] = total
    record["unpacked_files"] = count
    record["top"] = sorted(os.listdir(root))
    for folder in sorted(os.listdir(root)):
        path = os.path.join(root, folder)
        if os.path.isdir(path):
            size, files = tree_bytes(path)
            record.setdefault("top_bytes", {})[folder] = {"bytes": size, "files": files}
    bin_dir = os.path.join(root, "bin")
    record["bin"] = {entry: os.path.getsize(os.path.join(bin_dir, entry))
                     for entry in sorted(os.listdir(bin_dir))
                     if os.path.isfile(os.path.join(bin_dir, entry))}
    licences = []
    for folder, _dirs, files in os.walk(root):
        for entry in files:
            if entry.upper().startswith(("LICENSE", "LICENCE", "COPYING", "NOTICE")):
                licences.append(os.path.relpath(os.path.join(folder, entry), root).replace(os.sep, "/"))
    record["licence_files"] = sorted(licences)
    # What the release says about the third-party code it carries: the size, the digest and the
    # lines of its LICENSES file that name a component or a licence.
    notices = os.path.join(root, "LICENSES")
    if os.path.isfile(notices):
        with open(notices, encoding="utf-8", errors="replace") as handle:
            text = handle.read()
        words = ("license", "licence", "copyright (c)", "gnu ", "apache", "mit ", "bsd", "llvm", "gmp", "mimalloc",
                 "libuv", "cadical", "zlib", "nanoda", "lean4lean", "leantar", "leangz")
        named = []
        for line in text.splitlines():
            stripped = " ".join(line.split())
            if stripped and len(stripped) < 140 and any(word in stripped.lower() for word in words):
                if stripped not in named:
                    named.append(stripped)
        phrases = ("is under the", "is distributed under", "are distributed under", "is licensed under",
                   "are licensed under", "is released under")
        components = []
        for line in text.splitlines():
            stripped = " ".join(line.split())
            if stripped and len(stripped) < 160 and any(phrase in stripped for phrase in phrases):
                if stripped not in components:
                    components.append(stripped)
        record["licenses_file"] = {"bytes": len(text.encode("utf-8")), "sha256": sha256_file(notices),
                                   "lines": text.count("\n"), "components": components[:120],
                                   "named": named[:60]}

    versions = {}
    for program, args in (("lean", ["--version"]), ("lake", ["--version"]), ("leanc", ["--version"]),
                          ("nanoda_bin", ["--version"]), ("leantar", ["--version"])):
        path = os.path.join(bin_dir, program + EXE)
        if not os.path.isfile(path):
            versions[program] = None
            continue
        out = run_measured([path] + args, timeout=30)
        text = (out["stdout"] + out["stderr"]).strip()
        versions[program] = {"exit": out["exit"], "wall_s": out["wall_s"], "text": text[:600]}
    record["programs"] = versions
    out = run_measured([os.path.join(bin_dir, "lean" + EXE), "--help"], timeout=60)
    record["lean_help"] = (out["stdout"] + out["stderr"])[:6000]

    write_json(record_path, record)
    root = root.replace("\\", "/")
    github_env = os.environ.get("GITHUB_ENV")
    if github_env:
        with open(github_env, "a", encoding="utf-8") as handle:
            handle.write(f"LEAN_ROOT={root}\n")
    print(root)
    return 0


if __name__ == "__main__":
    sys.exit(main())
