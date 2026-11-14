#!/usr/bin/env python3
"""Fetches the PDF responses cc-pdfs.json names from one Common Crawl WARC file, each by a
range request for its gzip member, and keeps the HTTP body when its SHA-256 matches the list.

    fetch_cc.py <cc-pdfs.json> <dir>

The documents are never committed: the list holds offsets and hashes only, and the bodies
come from data.commoncrawl.org when the tests run. A document already in <dir> with the
listed hash is kept. Each request is tried a few times with a growing pause and then given
up; the exit code is the number of documents that could not be fetched (0: all of them).
"""

import gzip
import hashlib
import json
import os
import sys
import time
import urllib.error
import urllib.request

ORIGIN = "https://data.commoncrawl.org/"
ATTEMPTS = 5
TIMEOUT_SECONDS = 120
USER_AGENT = "corpus-pdf-tests/1.0"


def body_of(member):
    """The HTTP body of a gzip member holding one WARC response record."""
    record = gzip.decompress(member)
    warc_end = record.index(b"\r\n\r\n")
    length = None
    for line in record[:warc_end].split(b"\r\n"):
        name, _, value = line.partition(b":")
        if name.strip().lower() == b"content-length":
            length = int(value.strip())
    if length is None:
        raise ValueError("no Content-Length in the WARC record")
    payload_start = warc_end + 4
    body_start = record.index(b"\r\n\r\n", payload_start) + 4
    return record[body_start:payload_start + length]


def fetch_member(url, offset, length):
    for attempt in range(1, ATTEMPTS + 1):
        request = urllib.request.Request(url, headers={"Range": "bytes=%d-%d" % (offset, offset + length - 1), "User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(request, timeout=TIMEOUT_SECONDS) as response:
                if response.status == 206:
                    data = response.read()
                    if len(data) == length:
                        return data, attempt
        except (urllib.error.URLError, OSError, ValueError):
            pass
        time.sleep(attempt * attempt)
    return None, ATTEMPTS


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 64
    with open(argv[1], encoding="utf-8") as handle:
        listing = json.load(handle)
    folder = argv[2]
    os.makedirs(folder, exist_ok=True)
    url = ORIGIN + listing["warc"]
    kept = fetched = retried = 0
    failed = []
    started = time.monotonic()
    for entry in listing["documents"]:
        target = os.path.join(folder, entry["name"])
        if os.path.exists(target):
            with open(target, "rb") as handle:
                if hashlib.sha256(handle.read()).hexdigest() == entry["sha256"]:
                    kept += 1
                    continue
        member, attempts = fetch_member(url, entry["offset"], entry["length"])
        retried += attempts - 1
        body = None
        if member is not None:
            try:
                body = body_of(member)
            except (OSError, ValueError, EOFError):
                body = None
        if body is None or hashlib.sha256(body).hexdigest() != entry["sha256"]:
            failed.append(entry["name"])
            print("  %s: %s" % (entry["name"], "not fetched" if body is None else "hash differs"), file=sys.stderr)
            continue
        with open(target, "wb") as handle:
            handle.write(body)
        fetched += 1
    print("%d documents: %d fetched, %d already there, %d failed, %d retries, %.0f s" % (
        len(listing["documents"]), fetched, kept, len(failed), retried, time.monotonic() - started))
    return min(len(failed), 125)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
