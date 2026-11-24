#!/usr/bin/env python3
"""Fetches the problems of the long-tail kit and packs them into a problem set.

    python -I make_set.py <work folder> [--tool <smt-live>] [--list <kit-list.tsv>]

1. Streams the archives of the two logics of the SMT-LIB release 2025 (non-incremental
   benchmarks, Zenodo record 16740866: QF_LIA.tar.zst, 687 MB, and QF_BV.tar.zst, 1.7 GB) once
   each through zstd (the zstd tool on the PATH, or Python's own zstd module), keeps the files the
   list (kit-list.tsv beside this script) names, checks each against its SHA-256 and writes them
   as <work folder>/problems/<id>.smt2. A file that is already there with its hash is not fetched
   again; when every file is there nothing is streamed. Nothing of the archives is kept but the
   listed files, and none of them is ever committed.
2. Writes <work folder>/ATTRIBUTION.tsv from each file's own metadata: the id, the path in the
   library, the licence its :license states (the collection's CC BY 4.0 when it states none), its
   :category and the first line of its :source.
3. Copies the files the list marks as the sample (the ones the build server solves) to
   <work folder>/sample/.
4. `smt-live pack <work folder>/problems --out <work folder>/problems.jsonl`: one row a problem,
   its id the list's, its expected verdict the problem's own :status. Every row must be taken by
   the reader the server reads a set with.

--tool names the tool; by default the Release build beside this kit
(bin/Release/net10.0/smt-live, `dotnet build -c Release` in the tool's folder).
"""

import hashlib
import importlib.util
import os
import re
import shutil
import subprocess
import sys
import tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_LICENCE = "https://creativecommons.org/licenses/by/4.0/"
SOURCE_CHARACTERS = 200

LICENSE_PATTERN = re.compile(r'\(\s*set-info\s+:license\s+"((?:[^"]|"")*)"\s*\)')
CATEGORY_PATTERN = re.compile(r'\(\s*set-info\s+:category\s+"((?:[^"]|"")*)"\s*\)')
SOURCE_PATTERN = re.compile(r"\(\s*set-info\s+:source\s+\|([^|]*)\|\s*\)")


def load_list_module():
    """make_list.py beside this script: the archives' address and the stream through zstd."""
    spec = importlib.util.spec_from_file_location("make_list", os.path.join(HERE, "make_list.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_list(path):
    with open(path, encoding="utf-8") as handle:
        header = handle.readline().rstrip("\r\n").split("\t")
        return [dict(zip(header, line.rstrip("\r\n").split("\t"))) for line in handle if line.strip()]


def tool_folder():
    return os.path.dirname(os.path.dirname(HERE))


def default_tool():
    name = "smt-live.exe" if os.name == "nt" else "smt-live"
    return os.path.join(tool_folder(), "bin", "Release", "net10.0", name)


def has(path, sha256):
    if not os.path.exists(path):
        return False
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest() == sha256


def fetch(rows, folder, make_list):
    """Fetches what is missing; returns the number of listed files that are not there after."""
    os.makedirs(folder, exist_ok=True)
    bad = 0
    for logic in make_list.LOGICS:
        members = {row["member"]: row for row in rows if row["logic"] == logic}
        missing = {name for name, row in members.items() if not has(os.path.join(folder, row["id"] + ".smt2"), row["sha256"])}
        if not missing:
            print("%s: all %d already here" % (logic, len(members)), flush=True)
            continue
        source = "%s/%s.tar.zst?download=1" % (make_list.ARCHIVES, logic)
        print("%s: streaming %s for %d file(s)" % (logic, source, len(missing)), flush=True)
        stream, close = make_list.zstd_stream(source)
        try:
            with tarfile.open(fileobj=stream, mode="r|") as archive:
                for member in archive:
                    if not member.isfile() or member.name not in missing:
                        continue
                    data = archive.extractfile(member).read()
                    row = members[member.name]
                    if hashlib.sha256(data).hexdigest() != row["sha256"]:
                        print("DIFFERS %s %s" % (row["id"], member.name))
                        continue
                    with open(os.path.join(folder, row["id"] + ".smt2"), "wb") as handle:
                        handle.write(data)
                    missing.discard(member.name)
                    if not missing:
                        break
        finally:
            close()
        for name in sorted(missing):
            print("MISSING %s %s" % (members[name]["id"], name))
            bad += 1
        print("%s: %d of %d here" % (logic, len(members) - len(missing), len(members)), flush=True)
    return bad


def attribution(text):
    """The licence, the category and the first line of the source a problem's text states."""
    licence = LICENSE_PATTERN.search(text)
    category = CATEGORY_PATTERN.search(text)
    source = SOURCE_PATTERN.search(text)
    first = ""
    if source:
        lines = [line.strip() for line in source.group(1).splitlines() if line.strip()]
        first = lines[0] if lines else ""
    first = " ".join(first.split())[:SOURCE_CHARACTERS]
    return (licence.group(1).replace('""', '"') if licence else DEFAULT_LICENCE,
            category.group(1) if category else "",
            first)


def write_attribution(rows, folder, path):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("id\tsmt-lib path\tlicence\tcategory\tsource\n")
        for row in rows:
            with open(os.path.join(folder, row["id"] + ".smt2"), encoding="utf-8", errors="replace") as problem:
                licence, category, source = attribution(problem.read())
            handle.write("\t".join((row["id"], row["member"], licence, category, source)) + "\n")


def main(argv):
    args = argv[1:]
    tool = default_tool()
    listing = os.path.join(HERE, "kit-list.tsv")
    for flag in ("--tool", "--list"):
        if flag in args:
            at = args.index(flag)
            if at + 1 >= len(args):
                print(__doc__, file=sys.stderr)
                return 64
            if flag == "--tool":
                tool = args[at + 1]
            else:
                listing = args[at + 1]
            del args[at:at + 2]
    if len(args) != 1:
        print(__doc__, file=sys.stderr)
        return 64

    work = os.path.abspath(args[0])
    folder = os.path.join(work, "problems")
    sample = os.path.join(work, "sample")
    problems = os.path.join(work, "problems.jsonl")
    rows = read_list(listing)

    if not os.path.exists(tool):
        print("the tool is not there: %s (dotnet build -c Release in %s, or --tool)" % (tool, tool_folder()), file=sys.stderr)
        return 1

    print("fetch: %s, %d problem(s)" % (folder, len(rows)), flush=True)
    bad = fetch(rows, folder, load_list_module())
    if bad:
        print("%d listed file(s) missing or different: the set is not whole" % bad, file=sys.stderr)
        return 1

    write_attribution(rows, folder, os.path.join(work, "ATTRIBUTION.tsv"))
    if os.path.isdir(sample):
        shutil.rmtree(sample)
    os.makedirs(sample)
    for row in rows:
        if row["sample"] == "1":
            shutil.copyfile(os.path.join(folder, row["id"] + ".smt2"), os.path.join(sample, row["id"] + ".smt2"))
    print("sample: %s, %d problem(s)" % (sample, sum(1 for row in rows if row["sample"] == "1")), flush=True)

    print("pack: %s" % problems, flush=True)
    status = subprocess.call([tool, "pack", folder, "--out", problems])
    if status != 0:
        print("smt-live pack exited with %d" % status, file=sys.stderr)
        return status

    with open(problems, encoding="utf-8") as handle:
        count = sum(1 for line in handle if line.strip())
    if count != len(rows):
        print("the set has %d rows, the list %d" % (count, len(rows)), file=sys.stderr)
        return 1
    digest = hashlib.sha256()
    with open(problems, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 22), b""):
            digest.update(chunk)
    print("the set: %s, %d rows, %d bytes, SHA-256 %s" % (problems, count, os.path.getsize(problems), digest.hexdigest()))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
