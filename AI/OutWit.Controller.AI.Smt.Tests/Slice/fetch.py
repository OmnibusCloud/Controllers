#!/usr/bin/env python3
"""Fetch the slice of SMT-LIB problems named by a fetch list from the SMT-LIB 2025 archives.

    fetch.py <fetch-list.tsv> <out-dir>

The list names 469 problems of the SMT-LIB release 2025 (non-incremental benchmarks, Zenodo
record 16740866, DOI 10.5281/zenodo.16740866; the collection is under CC BY 4.0, each
benchmark's own licence is in its :license field): for each an id, the logic, the path inside
the logic's archive, the size, the SHA-256, the expected verdict and the licence. No benchmark
text is in the list, and none is ever committed.

Streams each logic's archive from Zenodo once through `zstd -dc` (the zstd tool must be on the
PATH), checks every listed member against the SHA-256 of the list and writes it as
<out-dir>/<id>.smt2 (first as <id>.smt2.part, renamed when it is whole); nothing else of the
archive is kept. A file that is already there is hashed: it is kept when it is the list's, and
fetched again when it is not (a file of an older list, a file a killed run left short). Also
writes <out-dir>/ATTRIBUTION.tsv (id, SMT-LIB path, licence).
Exits 1 when a listed file is missing or differs.
"""

import hashlib
import os
import subprocess
import sys
import tarfile
import threading
import urllib.request

ARCHIVES = "https://zenodo.org/records/16740866/files"
USER_AGENT = "smt-slice-fetch/1.0"


def read_list(path):
    with open(path, encoding="utf-8") as handle:
        header = handle.readline().rstrip("\r\n").split("\t")
        return [dict(zip(header, line.rstrip("\r\n").split("\t"))) for line in handle if line.strip()]


def has(path, sha256):
    """Whether a file is there with the SHA-256 the list gives it."""
    if not os.path.exists(path):
        return False
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest() == sha256


def missing_members(members, out):
    """The members of one logic whose file is not there with the list's SHA-256."""
    return {name for name, row in members.items() if not has(os.path.join(out, row["id"] + ".smt2"), row["sha256"])}


def keep(out, row, data):
    """Writes a fetched file whole or not at all: to <id>.smt2.part, then renamed."""
    path = os.path.join(out, row["id"] + ".smt2")
    with open(path + ".part", "wb") as handle:
        handle.write(data)
    os.replace(path + ".part", path)


def pump(url, sink):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            while True:
                chunk = response.read(1 << 20)
                if not chunk:
                    break
                sink.write(chunk)
    except (BrokenPipeError, OSError):
        pass  # the reader stopped early: every listed file was found
    finally:
        try:
            sink.close()
        except OSError:
            pass


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 64
    rows = read_list(argv[1])
    out = argv[2]
    os.makedirs(out, exist_ok=True)
    wanted = {}
    for row in rows:
        wanted.setdefault(row["logic"], {})[row["member"]] = row
    bad = 0
    for logic in sorted(wanted):
        members = wanted[logic]
        missing = missing_members(members, out)
        if not missing:
            print("%s: all %d already here" % (logic, len(members)), flush=True)
            continue
        url = "%s/%s.tar.zst?download=1" % (ARCHIVES, logic)
        zstd = subprocess.Popen(["zstd", "-dc"], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
        thread = threading.Thread(target=pump, args=(url, zstd.stdin), daemon=True)
        thread.start()
        with tarfile.open(fileobj=zstd.stdout, mode="r|") as archive:
            for member in archive:
                if member.name not in missing:
                    continue
                data = archive.extractfile(member).read()
                row = members[member.name]
                if hashlib.sha256(data).hexdigest() != row["sha256"]:
                    print("DIFFERS %s %s" % (row["id"], member.name))
                    bad += 1
                    continue
                keep(out, row, data)
                missing.discard(member.name)
                if not missing:
                    break
        zstd.kill()
        thread.join(timeout=5)
        for name in sorted(missing):
            print("MISSING %s %s" % (members[name]["id"], name))
            bad += 1
        print("%s: %d of %d fetched" % (logic, len(members) - len(missing), len(members)), flush=True)
    with open(os.path.join(out, "ATTRIBUTION.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("id\tsmt-lib path\tlicence\n")
        for row in rows:
            handle.write("%s\t%s\t%s\n" % (row["id"], row["member"], row["license"]))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
