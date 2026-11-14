#!/usr/bin/env python3
"""Fetch the SM-0 benchmark slice named by a fetch list from the SMT-LIB 2025 archives.

    fetch.py <pins.env> <fetch-list.tsv> <out-dir>

Streams each logic's archive from Zenodo once through `zstd -dc`, writes every listed member as
<out-dir>/<id>.smt2 and checks its SHA-256 against the list; nothing else of the archive is
kept. Also writes <out-dir>/ATTRIBUTION.tsv (id, SMT-LIB path, licence) for the report.
Exits 1 when a listed file is missing or differs.
"""

import hashlib
import os
import subprocess
import sys
import tarfile
import threading
import urllib.request


def read_pins(path):
    pins = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                pins[key] = value
    return pins


def read_list(path):
    with open(path, encoding="utf-8") as handle:
        header = handle.readline().rstrip("\n").split("\t")
        return [dict(zip(header, line.rstrip("\n").split("\t"))) for line in handle if line.strip()]


def pump(url, sink):
    request = urllib.request.Request(url, headers={"User-Agent": "smt-sm0-spike"})
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
    if len(argv) != 4:
        print(__doc__, file=sys.stderr)
        return 64
    pins = read_pins(argv[1])
    rows = read_list(argv[2])
    out = argv[3]
    os.makedirs(out, exist_ok=True)
    wanted = {}
    for row in rows:
        wanted.setdefault(row["logic"], {})[row["member"]] = row
    bad = 0
    for logic in sorted(wanted):
        members = wanted[logic]
        missing = {name for name in members if not os.path.exists(os.path.join(out, members[name]["id"] + ".smt2"))}
        if not missing:
            print("%s: all %d already here" % (logic, len(members)), flush=True)
            continue
        url = "%s/%s.tar.zst?download=1" % (pins["SMTLIB_URL_PREFIX"], logic)
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
                with open(os.path.join(out, row["id"] + ".smt2"), "wb") as handle:
                    handle.write(data)
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
