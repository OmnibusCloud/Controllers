#!/usr/bin/env python3
"""Selects the problems of the long-tail kit and writes its list, kit-list.tsv.

    python -I make_list.py <catalog.sqlite> <work folder> [--out <kit-list.tsv>] [--archive LOGIC=PATH ...]

Two pinned inputs, nothing else; the same inputs give the same list, byte for byte.

1. The SMT-LIB Catalog 2025 (Zenodo record 16290040, DOI 10.5281/zenodo.16290040, CC BY 4.0):
   the SQLite database of the benchmark library's metadata and of the results of every SMT-COMP
   since 2005, by Hans-Joerg Schurr and others (described in "Exploring the SMT-LIB Benchmark
   Library", TACAS 2026). The archive SMT-LIB-Catalog-2025.tar.zst (1,199,613,453 bytes, MD5
   50b0ff03107a4f0c3274d48d2b556705 as Zenodo states it, SHA-256 CATALOG_ARCHIVE_SHA256) holds
   smtlib2025.sqlite (5,830,533,120 bytes, SHA-256 CATALOG_SHA256), which this script reads and
   holds to its SHA-256 first.
2. The SMT-LIB release 2025, non-incremental benchmarks (Zenodo record 16740866, DOI
   10.5281/zenodo.16740866), the archives QF_LIA.tar.zst and QF_BV.tar.zst: streamed once each
   through zstd (the zstd tool on the PATH, or Python's own zstd module), every candidate's file
   held to the catalog's size and its own :status, and hashed. Nothing of the archives is kept
   but a table of the candidates' sizes and hashes in the work folder, which a second run reads
   instead of streaming again. --archive LOGIC=PATH reads a local copy of an archive instead.

The rule, the same for every problem (see RULE below and the kit's SCRIPT.md):

- a non-incremental benchmark of QF_LIA or QF_BV with one check-sat and a declared :status of
  sat or unsat, which the status the catalog infers from the competitions' answers does not
  contradict, under a licence that allows the kit's use (LICENCES), of at most MAX_BYTES bytes;
- its competition time is Z3's own: the CPU time of the solver Z3 (not a solver built on it) at
  the newest SMT-COMP in which Z3 ran on the benchmark (2014 to 2022 for these logics), the
  fastest of Z3's entries there that answered the declared status; a benchmark Z3 did not solve
  there, or answered wrongly, has no time and is no candidate;
- that time is between MIN_SECONDS and MAX_SECONDS.

From the candidates, each logic is filled in rounds: every round takes one more problem, in a
fixed pseudo-random order (the SHA-256 of SEED and the problem's path), from each cell of
(family, status, time band) that still has one, while the family holds fewer than FAMILY_CAP and
the logic fewer than its target. QF_LIA's target is LOGIC_TARGET; QF_BV fills the kit up to
KIT_PROBLEMS. The rounds give every family its share before a large one gives more, both
statuses within a family, and every time band. SAMPLE_PER_GROUP problems of each logic and
status, spread over their times, are marked as the sample the build server solves.
"""

import hashlib
import os
import re
import shutil
import sqlite3
import subprocess
import sys
import tarfile
import threading
import urllib.request
from collections import Counter, defaultdict

CATALOG_SHA256 = "f6d26a67996c006dac2933b93cbe2d96484603c75e8bc27b6bdb1a628f5ea0c9"
CATALOG_ARCHIVE_SHA256 = "15c7ba8e6e47f36d3851a437e0e0f1dbac3238f7c73d07fe25b377513d98c3c6"
CATALOG_NAME = "SMT-LIB Catalog 2025"
ARCHIVES = "https://zenodo.org/records/16740866/files"
USER_AGENT = "smt-kit-list/1.0"

LOGICS = ("QF_LIA", "QF_BV")
PREFIXES = {"QF_LIA": "lia", "QF_BV": "bv"}
KIT_PROBLEMS = 2000
LOGIC_TARGET = 1000
FAMILY_CAP = 200
MIN_SECONDS = 1.0
MAX_SECONDS = 60.0
BAND_EDGES = (1.0, 2.0, 5.0, 10.0, 20.0, 40.0, 60.0)
MAX_BYTES = 512 << 10
SEED = "smt-kit-a/2025"
SAMPLE_PER_GROUP = 10

# The solver Z3 in the catalog's table of solvers, by name: not Z3alpha, Z3++, Z3-Noodler and
# the other solvers built on it, which the catalog lists as solvers of their own.
SOLVER = "Z3"

# The licences, by SPDX identifier, that let a lab fetch, run and report a problem: the
# collection's default and the permissive ones. Not CC BY-NC (commercial use), not GPL, not a
# licence the catalog cannot name.
LICENCES = ("CC-BY-4.0", "CC-BY-SA-4.0", "CC0-1.0", "MIT", "Apache-2.0", "ISC")

COLUMNS = ("id", "logic", "member", "bytes", "sha256", "status", "time_s", "wall_s", "time_source",
           "license", "category", "family", "band", "sample")

RULE = ("Z3's CPU time at the newest SMT-COMP in which Z3 ran on the benchmark, the fastest of its "
        "entries there that answered the declared status, between %g s and %g s" % (MIN_SECONDS, MAX_SECONDS))

STATUS_PATTERN = re.compile(rb"\(\s*set-info\s+:status\s+(sat|unsat|unknown)\s*\)")


def member_path(logic, folder, name):
    """The path of a benchmark inside its logic's archive of the release."""
    return "non-incremental/%s/%s/%s" % (logic, folder, name)


def band_of(seconds):
    """The time band of a competition time: [1, 2), [2, 5), ... [40, 60]."""
    for low, high in zip(BAND_EDGES, BAND_EDGES[1:]):
        if seconds < high or high == BAND_EDGES[-1]:
            return "%g-%g" % (low, high)
    return "%g-%g" % (BAND_EDGES[-2], BAND_EDGES[-1])


def order_key(member):
    """The fixed pseudo-random order of the rounds: the SHA-256 of the seed and the path."""
    return hashlib.sha256((SEED + "\n" + member).encode("utf-8")).hexdigest()


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            chunk = handle.read(1 << 22)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


# ---------------------------------------------------------------------------------------------
# The catalog


def read_candidates(db, counts):
    """The candidates of the catalog by the rule, before the archives are read.

    db: an open sqlite3 connection to the catalog. counts: a Counter the reasons a benchmark is
    no candidate are added to, by (logic, reason). Returns a list of dicts.
    """
    solver = db.execute("select id from Solvers where name = ?", (SOLVER,)).fetchone()
    if solver is None:
        raise SystemExit("the catalog has no solver named %s" % SOLVER)
    evaluations = {row[0]: (row[1], row[2]) for row in db.execute(
        "select id, name, date from Evaluations where name like 'SMT-COMP %'")}

    marks = ",".join("?" * len(LOGICS))
    benchmarks = {}
    for row in db.execute(
            "select q.id, b.logic, f.folderName, b.name, b.size, q.status, q.inferredStatus, l.spdxIdentifier, l.link, b.category "
            "from Benchmarks b join Queries q on q.benchmark = b.id join Families f on f.id = b.family "
            "left join Licenses l on l.id = b.license "
            "where b.logic in (%s) and not b.isIncremental and b.queryCount = 1" % marks, LOGICS):
        query, logic, folder, name, size, status, inferred, spdx, link, category = row
        benchmarks[query] = {
            "query": query, "logic": logic, "family": folder, "member": member_path(logic, folder, name),
            "bytes": size, "status": status, "inferred": inferred, "spdx": spdx or "", "license": link or "",
            "category": category or ""}

    results = defaultdict(list)
    for query, evaluation, variant, cpu, wall, status in db.execute(
            "select r.query, r.evaluation, v.fullName, r.cpuTime, r.wallclockTime, r.status "
            "from Results r join SolverVariants v on v.id = r.solverVariant where v.solver = ?", (solver[0],)):
        if query in benchmarks and evaluation in evaluations and cpu is not None:
            results[query].append((evaluations[evaluation][1], evaluations[evaluation][0], variant, cpu, wall, status))

    candidates = []
    for query in sorted(benchmarks):
        item = benchmarks[query]
        logic = item["logic"]
        counts[(logic, "benchmarks")] += 1
        reason = refusal(item, results.get(query, []))
        if reason is not None:
            counts[(logic, reason)] += 1
            continue
        candidates.append(item)
    return candidates


def refusal(item, runs):
    """Why a benchmark is no candidate, or None; fills in its time when it is one."""
    status = item["status"]
    if status not in ("sat", "unsat"):
        return "1 no declared status"
    if item["inferred"] not in (None, status):
        return "2 the competitions' answers contradict the status"
    if not runs:
        return "3 Z3 never ran on it"
    newest = max(run[0] for run in runs)
    there = [run for run in runs if run[0] == newest]
    solved = [run for run in there if run[5] == status]
    if not solved:
        wrong = [run for run in there if run[5] in ("sat", "unsat")]
        return "4 Z3 answered wrongly at its newest run" if wrong else "4 Z3 did not solve it at its newest run"
    best = min(solved, key=lambda run: (run[3], run[2]))
    if best[3] < MIN_SECONDS:
        return "5 Z3 took under %g s" % MIN_SECONDS
    if best[3] > MAX_SECONDS:
        return "5 Z3 took over %g s" % MAX_SECONDS
    if item["spdx"] not in LICENCES:
        return "6 in the band, under another licence"
    if item["bytes"] is None or item["bytes"] > MAX_BYTES:
        return "7 in the band, larger than the size cap"
    item["time_s"] = best[3]
    item["wall_s"] = best[4]
    item["time_source"] = "%s %s" % (best[1], best[2])
    item["band"] = band_of(best[3])
    return None


# ---------------------------------------------------------------------------------------------
# The archives


def zstd_stream(source):
    """A readable stream of the decompressed archive and a function that ends it.

    source: a local path or a URL. Uses the zstd tool when it is on the PATH, else Python's own
    zstd module (compression.zstd from 3.14, the backports.zstd or zstandard packages).
    """
    if os.path.exists(source):
        raw = open(source, "rb")
    else:
        request = urllib.request.Request(source, headers={"User-Agent": USER_AGENT})
        raw = urllib.request.urlopen(request, timeout=300)

    tool = shutil.which("zstd")
    if tool:
        process = subprocess.Popen([tool, "-dc"], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
        thread = threading.Thread(target=pump, args=(raw, process.stdin), daemon=True)
        thread.start()

        def close():
            process.kill()
            thread.join(timeout=5)
            raw.close()
        return process.stdout, close

    module = None
    for name in ("compression.zstd", "backports.zstd"):
        try:
            module = __import__(name, fromlist=["ZstdFile"])
            break
        except ImportError:
            continue
    if module is not None:
        stream = module.ZstdFile(raw, "rb")
    else:
        try:
            import zstandard
        except ImportError:
            raise SystemExit("no zstd: put the zstd tool on the PATH, or use a Python with compression.zstd, backports.zstd or zstandard")
        stream = zstandard.ZstdDecompressor().stream_reader(raw)

    def close_module():
        stream.close()
        raw.close()
    return stream, close_module


def pump(raw, sink):
    try:
        while True:
            chunk = raw.read(1 << 20)
            if not chunk:
                break
            sink.write(chunk)
    except (BrokenPipeError, OSError, ValueError):
        pass  # the reader stopped early
    finally:
        try:
            sink.close()
        except OSError:
            pass


class Counted:
    """A readable stream that counts the bytes it gave."""

    def __init__(self, stream):
        self.stream = stream
        self.given = 0

    def read(self, size=-1):
        data = self.stream.read(size)
        self.given += len(data)
        return data


def scan(tar_stream, wanted):
    """Reads a tar stream and describes every member that is wanted.

    wanted: a set of member paths. Returns {member: (bytes, sha256, status in its text or "")}.
    The stream must be the whole archive: a tar ends with blocks of zeros after its last member,
    and a stream that ends where a member's header would start (a download cut between two
    members) is refused, not taken for an archive with fewer members (whose table would be
    written short and read again by the next run).
    """
    found = {}
    counted = Counted(tar_stream)
    with tarfile.open(fileobj=counted, mode="r|") as archive:
        for member in archive:
            if not member.isfile() or member.name not in wanted:
                continue
            data = archive.extractfile(member).read()
            match = STATUS_PATTERN.search(data)
            found[member.name] = (len(data), hashlib.sha256(data).hexdigest(), match.group(1).decode("ascii") if match else "")
        if counted.given <= archive.offset:
            raise SystemExit("the stream ended inside the archive, after %d bytes and %d wanted member(s): nothing of it is kept; run again" % (
                counted.given, len(found)))
    return found


def read_members(path):
    members = {}
    with open(path, encoding="utf-8") as handle:
        handle.readline()
        for line in handle:
            member, size, sha256, status = line.rstrip("\r\n").split("\t")
            members[member] = (int(size), sha256, status)
    return members


def write_members(path, members):
    with open(path + ".part", "w", encoding="utf-8", newline="\n") as handle:
        handle.write("member\tbytes\tsha256\tstatus\n")
        for member in sorted(members):
            size, sha256, status = members[member]
            handle.write("%s\t%d\t%s\t%s\n" % (member, size, sha256, status))
    os.replace(path + ".part", path)


def match(candidates, members, counts):
    """The candidates whose file the release holds as the catalog describes it, with its hash."""
    matched = []
    for item in candidates:
        logic = item["logic"]
        found = members.get(item["member"])
        if found is None:
            counts[(logic, "8 in the band, not in the release's archive")] += 1
            continue
        size, sha256, status = found
        if size != item["bytes"]:
            counts[(logic, "8 in the band, the archive's file is another size")] += 1
            continue
        if status != item["status"]:
            counts[(logic, "8 in the band, the archive's file declares another status")] += 1
            continue
        item["sha256"] = sha256
        matched.append(item)
    return matched


# ---------------------------------------------------------------------------------------------
# The pick


def fill(candidates, target):
    """One logic's problems by the rounds over (family, status, band) cells."""
    cells = defaultdict(list)
    for item in candidates:
        cells[(item["family"], item["status"], item["band"])].append(item)
    for queue in cells.values():
        queue.sort(key=lambda item: order_key(item["member"]))
    order = sorted(cells, key=lambda cell: order_key("\t".join(cell)))
    families = Counter()
    picked = []
    while len(picked) < target:
        took = False
        for cell in order:
            queue = cells[cell]
            if not queue or families[cell[0]] >= FAMILY_CAP:
                continue
            picked.append(queue.pop(0))
            families[cell[0]] += 1
            took = True
            if len(picked) == target:
                break
        if not took:
            break
    return picked


def pick(candidates):
    """The kit: its rows in list order, with ids and the sample marked."""
    by_logic = defaultdict(list)
    for item in candidates:
        by_logic[item["logic"]].append(item)
    rows = []
    left = KIT_PROBLEMS
    for position, logic in enumerate(LOGICS):
        target = LOGIC_TARGET if position < len(LOGICS) - 1 else left
        picked = sorted(fill(by_logic[logic], min(target, left)), key=lambda item: item["member"])
        left -= len(picked)
        for number, item in enumerate(picked):
            row = dict(item)
            row["id"] = "%s%04d" % (PREFIXES[logic], number)
            row["sample"] = 0
            rows.append(row)
    mark_sample(rows)
    return rows


def mark_sample(rows):
    """SAMPLE_PER_GROUP rows of each logic and status, at evenly spaced places of their times."""
    groups = defaultdict(list)
    for row in rows:
        groups[(row["logic"], row["status"])].append(row)
    for group in groups.values():
        group.sort(key=lambda row: (row["time_s"], row["member"]))
        count = min(SAMPLE_PER_GROUP, len(group))
        for index in range(count):
            group[(2 * index + 1) * len(group) // (2 * count)]["sample"] = 1


def write_list(path, rows):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\t".join(COLUMNS) + "\n")
        for row in rows:
            values = dict(row)
            values["time_s"] = "%.3f" % row["time_s"]
            values["wall_s"] = "" if row["wall_s"] is None else "%.3f" % row["wall_s"]
            handle.write("\t".join(str(values[column]) for column in COLUMNS) + "\n")


def summary(rows):
    lines = []
    for logic in LOGICS:
        mine = [row for row in rows if row["logic"] == logic]
        status = Counter(row["status"] for row in mine)
        families = Counter(row["family"] for row in mine)
        bands = Counter(row["band"] for row in mine)
        sizes = sorted(row["bytes"] for row in mine) or [0]
        lines.append("%s: %d problems, sat %d, unsat %d, %d families (the largest %s), sample %d" % (
            logic, len(mine), status["sat"], status["unsat"], len(families),
            ", ".join("%s %d" % pair for pair in families.most_common(3)), sum(row["sample"] for row in mine)))
        lines.append("  bands: %s" % ", ".join("%s s %d" % (band_of(low), bands[band_of(low)]) for low in BAND_EDGES[:-1]))
        lines.append("  bytes: %d in all, median %d, largest %d" % (sum(sizes), sizes[len(sizes) // 2], sizes[-1]))
    return lines


# ---------------------------------------------------------------------------------------------


def main(argv):
    args = argv[1:]
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "kit-list.tsv")
    archives = {}
    while "--out" in args or "--archive" in args:
        for flag in ("--out", "--archive"):
            if flag in args:
                at = args.index(flag)
                if at + 1 >= len(args):
                    print(__doc__, file=sys.stderr)
                    return 64
                value = args[at + 1]
                del args[at:at + 2]
                if flag == "--out":
                    out = value
                else:
                    logic, _, path = value.partition("=")
                    archives[logic] = path
    if len(args) != 2:
        print(__doc__, file=sys.stderr)
        return 64
    catalog, work = args
    os.makedirs(work, exist_ok=True)

    print("catalog: %s" % catalog, flush=True)
    digest = sha256_file(catalog)
    if digest != CATALOG_SHA256:
        print("the catalog hashes to %s, not the pinned %s (%s)" % (digest, CATALOG_SHA256, CATALOG_NAME), file=sys.stderr)
        return 1

    counts = Counter()
    db = sqlite3.connect("file:%s?mode=ro" % catalog, uri=True)
    try:
        candidates = read_candidates(db, counts)
    finally:
        db.close()

    members = {}
    for logic in LOGICS:
        wanted = {item["member"] for item in candidates if item["logic"] == logic}
        table = os.path.join(work, "%s-members.tsv" % logic)
        if os.path.exists(table):
            print("%s: the members' table is there: %s" % (logic, table), flush=True)
            found = read_members(table)
        else:
            source = archives.get(logic, "%s/%s.tar.zst?download=1" % (ARCHIVES, logic))
            print("%s: streaming %s for %d candidates" % (logic, source, len(wanted)), flush=True)
            stream, close = zstd_stream(source)
            try:
                found = scan(stream, wanted)
            finally:
                close()
            write_members(table, found)
        members.update(found)

    matched = match(candidates, members, counts)
    rows = pick(matched)
    write_list(out, rows)

    for logic in LOGICS:
        print("%s:" % logic)
        for (name, reason), count in sorted(counts.items()):
            if name == logic:
                print("  %-48s %d" % (reason, count))
        print("  %-48s %d" % ("candidates matched in the release", sum(1 for item in matched if item["logic"] == logic)))
    for line in summary(rows):
        print(line)
    print("the list: %s, %d rows, SHA-256 %s" % (out, len(rows), sha256_file(out)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
