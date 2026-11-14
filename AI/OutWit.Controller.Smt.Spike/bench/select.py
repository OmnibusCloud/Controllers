#!/usr/bin/env python3
"""Choose the SM-0 benchmark slice from the SMT-LIB 2025 release (non-incremental).

    select.py <pins.env> <native z3> <out-dir> [--seed N] [--jobs N]

Streams each logic's archive from Zenodo through `zstd -dc` (nothing is stored but the chosen
candidates), keeps, per benchmark family, a seeded reservoir of files that are small enough, have
one check-sat and a known :status (sat or unsat). The native Z3 release then solves every
candidate with a short time limit; a candidate stays when it answers its :status within the
limit. From those the slice is drawn per logic, alternating sat and unsat, spread over families
and over three bands of native time.

Writes <out-dir>/fetch-list.tsv (the slice: logic, member path, bytes, SHA-256, status, native
milliseconds, licence, category) and <out-dir>/select-report.md. The benchmarks themselves are
not part of the output. The fetch list is what the spike commits.
"""

import argparse
import concurrent.futures
import hashlib
import os
import random
import re
import subprocess
import sys
import tarfile
import threading
import time
import json
import urllib.request

# Problems per logic in the slice (500 in all).
TARGETS = [
    ("QF_LIA", 150), ("QF_BV", 150), ("QF_LRA", 40), ("QF_UF", 40), ("QF_AX", 20),
    ("QF_DT", 25), ("QF_ALIA", 15), ("QF_UFLIA", 20), ("QF_S", 20), ("QF_UFDT", 20),
]
MAX_BYTES = 512 * 1024
PER_FAMILY = 16
CANDIDATE_FACTOR = 3
NATIVE_LIMIT_S = 4
KEEP_BELOW_MS = 3000
BANDS_MS = (50, 500)
DEFAULT_LICENSE = "CC BY 4.0 (the SMT-LIB collection's default)"

STATUS = re.compile(rb"\(\s*set-info\s+:status\s+(sat|unsat|unknown)\s*\)")
LICENSE = re.compile(rb"\(\s*set-info\s+:license\s+\"([^\"]*)\"\s*\)")
CATEGORY = re.compile(rb"\(\s*set-info\s+:category\s+\"([^\"]*)\"\s*\)")
CHECK_SAT = re.compile(rb"\(\s*check-sat\s*\)")
INCREMENTAL = re.compile(rb"\(\s*(push|pop|check-sat-assuming|reset)\b")


def read_pins(path):
    pins = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                pins[key] = value
    return pins


def pump(url, sink, digest):
    """Downloads url into sink (zstd's stdin), hashing what it sends."""
    request = urllib.request.Request(url, headers={"User-Agent": "smt-sm0-spike"})
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            while True:
                chunk = response.read(1 << 20)
                if not chunk:
                    break
                digest.update(chunk)
                sink.write(chunk)
    finally:
        sink.close()


def stream_members(url):
    """Yields (member name, bytes) of every .smt2 file of a tar.zst archive at url that is at
    most MAX_BYTES long, reading the archive once as a stream. Returns the archive's MD5."""
    digest = hashlib.md5()
    zstd = subprocess.Popen(["zstd", "-dc"], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    thread = threading.Thread(target=pump, args=(url, zstd.stdin, digest), daemon=True)
    thread.start()
    with tarfile.open(fileobj=zstd.stdout, mode="r|") as archive:
        for member in archive:
            if not member.isfile() or not member.name.endswith(".smt2") or member.size > MAX_BYTES:
                continue
            handle = archive.extractfile(member)
            if handle is not None:
                yield member.name, handle.read()
    # The tar stream ends before zstd's output does (record padding): drain it, or the
    # download thread would wait on a full pipe.
    while zstd.stdout.read(1 << 20):
        pass
    thread.join()
    zstd.wait()
    yield None, digest.hexdigest()


def family_of(name):
    parts = name.split("/")
    # non-incremental/<logic>/<family>/.../<file>
    return "/".join(parts[2:4]) if len(parts) > 4 else "/".join(parts[2:3])


def candidates(url, rng):
    """Seeded reservoirs per family of usable files; returns (list, archive md5, seen)."""
    reservoirs = {}
    counts = {}
    seen = 0
    md5 = None
    for name, data in stream_members(url):
        if name is None:
            md5 = data
            break
        seen += 1
        status = STATUS.search(data)
        if not status or status.group(1) == b"unknown":
            continue
        if len(CHECK_SAT.findall(data)) != 1 or INCREMENTAL.search(data):
            continue
        family = family_of(name)
        counts[family] = counts.get(family, 0) + 1
        reservoir = reservoirs.setdefault(family, [])
        item = (name, data, status.group(1).decode())
        if len(reservoir) < PER_FAMILY:
            reservoir.append(item)
        else:
            slot = rng.randrange(counts[family])
            if slot < PER_FAMILY:
                reservoir[slot] = item
    pool = [item for family in sorted(reservoirs) for item in reservoirs[family]]
    return pool, md5, seen


def solve(z3, path):
    start = time.perf_counter()
    try:
        result = subprocess.run([z3, "-smt2", "-T:%d" % NATIVE_LIMIT_S, path], capture_output=True,
                                timeout=NATIVE_LIMIT_S + 10)
        output = result.stdout.decode("utf-8", "replace").strip().splitlines()
        verdict = output[0].strip() if output else "none"
    except subprocess.TimeoutExpired:
        verdict = "timeout"
    return verdict, int((time.perf_counter() - start) * 1000)


def band(milliseconds):
    return 0 if milliseconds < BANDS_MS[0] else 1 if milliseconds < BANDS_MS[1] else 2


def draw(accepted, target, rng):
    """Alternates sat and unsat; within each, round-robin over (time band, family)."""
    pools = {}
    for row in accepted:
        pools.setdefault(row["status"], []).append(row)
    queues = {}
    for status, rows in pools.items():
        groups = {}
        for row in rows:
            groups.setdefault((band(row["native_ms"]), row["family"]), []).append(row)
        keys = sorted(groups)
        rng.shuffle(keys)
        for key in keys:
            rng.shuffle(groups[key])
        order = []
        while any(groups[key] for key in keys):
            for key in keys:
                if groups[key]:
                    order.append(groups[key].pop())
        queues[status] = order
    chosen = []
    statuses = ["sat", "unsat"]
    while len(chosen) < target and any(queues.get(s) for s in statuses):
        for status in statuses:
            if queues.get(status) and len(chosen) < target:
                chosen.append(queues[status].pop(0))
    return sorted(chosen, key=lambda row: row["member"])


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("pins")
    parser.add_argument("z3")
    parser.add_argument("out")
    parser.add_argument("--seed", type=int, default=20261114)
    parser.add_argument("--jobs", type=int, default=os.cpu_count() or 2)
    args = parser.parse_args(argv[1:])

    pins = read_pins(args.pins)
    record = json.load(urllib.request.urlopen(urllib.request.Request(
        "https://zenodo.org/api/records/%s" % pins["SMTLIB_RECORD"], headers={"User-Agent": "smt-sm0-spike"}), timeout=60))
    stated = {f["key"]: f.get("checksum", "") for f in record.get("files", [])}
    os.makedirs(args.out, exist_ok=True)
    work = os.path.join(args.out, "candidates")
    os.makedirs(work, exist_ok=True)
    rows = []
    report = ["# SM-0 benchmark slice", "",
              "SMT-LIB release 2025, non-incremental (Zenodo record %s); seed %d; files up to %d bytes;"
              " native Z3 limit %d s, kept below %d ms." % (pins["SMTLIB_RECORD"], args.seed, MAX_BYTES,
                                                           NATIVE_LIMIT_S, KEEP_BELOW_MS), "",
              "| logic | archive MD5 | .smt2 files read | candidates | solved by native | chosen | sat | unsat |",
              "|---|---|---|---|---|---|---|---|"]
    for logic, target in TARGETS:
        rng = random.Random("%d/%s" % (args.seed, logic))
        url = "%s/%s.tar.zst?download=1" % (pins["SMTLIB_URL_PREFIX"], logic)
        started = time.time()
        pool, md5, seen = candidates(url, rng)
        rng.shuffle(pool)
        pool = pool[:target * CANDIDATE_FACTOR]
        print("%s: %d files read, %d candidates in %.0f s (md5 %s)" % (logic, seen, len(pool), time.time() - started, md5),
              flush=True)
        if stated.get(logic + ".tar.zst") != "md5:" + md5:
            print("%s.tar.zst: MD5 %s is not the one Zenodo states (%s)" % (logic, md5, stated.get(logic + ".tar.zst")))
            return 1
        jobs = []
        for index, (name, data, status) in enumerate(pool):
            path = os.path.join(work, "%s-%04d.smt2" % (logic, index))
            with open(path, "wb") as handle:
                handle.write(data)
            jobs.append((name, data, status, path))
        accepted = []
        with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as executor:
            futures = {executor.submit(solve, args.z3, job[3]): job for job in jobs}
            for future in concurrent.futures.as_completed(futures):
                name, data, status, path = futures[future]
                verdict, milliseconds = future.result()
                os.remove(path)
                if verdict != status or milliseconds > KEEP_BELOW_MS:
                    continue
                license_ = LICENSE.search(data)
                category = CATEGORY.search(data)
                accepted.append({
                    "logic": logic, "member": name, "family": family_of(name), "bytes": len(data),
                    "sha256": hashlib.sha256(data).hexdigest(), "status": status, "native_ms": milliseconds,
                    "license": license_.group(1).decode("utf-8", "replace") if license_ else DEFAULT_LICENSE,
                    "category": category.group(1).decode("utf-8", "replace") if category else "",
                })
        chosen = draw(accepted, target, rng)
        rows.extend(chosen)
        report.append("| %s | `%s` | %d | %d | %d | %d | %d | %d |" % (
            logic, md5, seen, len(pool), len(accepted), len(chosen),
            sum(1 for r in chosen if r["status"] == "sat"), sum(1 for r in chosen if r["status"] == "unsat")))
        print("%s: %d accepted, %d chosen" % (logic, len(accepted), len(chosen)), flush=True)

    columns = ["id", "logic", "member", "bytes", "sha256", "status", "native_ms", "license", "category"]
    with open(os.path.join(args.out, "fetch-list.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\t".join(columns) + "\n")
        for index, row in enumerate(rows):
            row["id"] = "p%03d" % index
            handle.write("\t".join(str(row[c]).replace("\t", " ") for c in columns) + "\n")
    licences = {}
    for row in rows:
        licences[row["license"]] = licences.get(row["license"], 0) + 1
    report += ["", "Licences of the chosen files:", ""]
    report += ["- %s: %d" % (name, count) for name, count in sorted(licences.items(), key=lambda kv: -kv[1])]
    with open(os.path.join(args.out, "select-report.md"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(report) + "\n")
    print("\n".join(report))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
