#!/usr/bin/env python3
"""Fetches the slice of SMT-LIB problems the Smt tests name and packs it into a problem set.

    python -I make_set.py <work folder> [--tool <smt-live>]

1. The tests' own fetcher (AI/OutWit.Controller.AI.Smt.Tests/Slice/fetch.py) reads the
   committed list beside it (fetch-list.tsv: 469 problems of the SMT-LIB release 2025, by
   path, size, SHA-256 and expected verdict), streams the logics' archives from Zenodo through
   `zstd -dc` (the zstd tool must be on the PATH), keeps the listed files, checks each against
   its SHA-256 and writes them as <work folder>/slice/<id>.smt2. A file that is already there is
   hashed, and fetched again unless it is the list's. No problem of the slice is in the
   repository, and none is to be committed.
2. `smt-live pack <work folder>/slice --out <work folder>/problems.jsonl`: one row a problem,
   its id the list's (p000 to p481), its expected verdict the problem's own :status.
3. Prints the set's rows, bytes and SHA-256, and holds them to the set EXPECTED.md names
   (SET_SHA256, SET_BYTES): the set a run of the kit is of, the name the provenance gives it.
   Exits 1 when the set is another.

--tool names the tool; by default the Release build beside this kit
(bin/Release/net10.0/smt-live, `dotnet build -c Release` in the tool's folder).
"""

import hashlib
import os
import subprocess
import sys

PROBLEMS = 469
SET_BYTES = 22_349_801
SET_SHA256 = "6772a20dc6dd6a19f32adeada38f0d15b9e3c92297e765b8eeeafef558150ac7"


def tool_folder():
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def default_tool():
    name = "smt-live.exe" if os.name == "nt" else "smt-live"
    return os.path.join(tool_folder(), "bin", "Release", "net10.0", name)


def describe(path):
    """The rows (lines that are not blank), the bytes and the SHA-256 of a set."""
    rows = 0
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for line in handle:
            digest.update(line)
            if line.strip():
                rows += 1
    return rows, os.path.getsize(path), digest.hexdigest()


def main(argv):
    args = argv[1:]
    tool = default_tool()
    if "--tool" in args:
        at = args.index("--tool")
        if at + 1 >= len(args):
            print(__doc__, file=sys.stderr)
            return 64
        tool = args[at + 1]
        del args[at:at + 2]
    if len(args) != 1:
        print(__doc__, file=sys.stderr)
        return 64

    work = os.path.abspath(args[0])
    slice_folder = os.path.join(work, "slice")
    problems = os.path.join(work, "problems.jsonl")
    tests = os.path.join(os.path.dirname(tool_folder()), "OutWit.Controller.AI.Smt.Tests", "Slice")
    fetcher = os.path.join(tests, "fetch.py")
    listing = os.path.join(tests, "fetch-list.tsv")

    if not os.path.exists(tool):
        print("the tool is not there: %s (dotnet build -c Release in %s, or --tool)" % (tool, tool_folder()), file=sys.stderr)
        return 1

    print("fetch: %s" % slice_folder, flush=True)
    status = subprocess.call([sys.executable, "-I", fetcher, listing, slice_folder])
    if status != 0:
        print("the fetcher exited with %d: the slice is not whole" % status, file=sys.stderr)
        return status

    print("pack: %s" % problems, flush=True)
    status = subprocess.call([tool, "pack", slice_folder, "--out", problems])
    if status != 0:
        print("smt-live pack exited with %d" % status, file=sys.stderr)
        return status

    rows, size, sha256 = describe(problems)
    print("the set: %s, %d rows, %d bytes, SHA-256 %s" % (problems, rows, size, sha256))
    if rows != PROBLEMS:
        print("the set has %d rows, not %d" % (rows, PROBLEMS), file=sys.stderr)
        return 1
    if sha256 != SET_SHA256 or size != SET_BYTES:
        print("the set is SHA-256 %s, %d bytes, not the kit's %s, %d bytes (EXPECTED.md)" % (sha256, size, SET_SHA256, SET_BYTES), file=sys.stderr)
        return 1

    print("the set is the kit's (EXPECTED.md)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
