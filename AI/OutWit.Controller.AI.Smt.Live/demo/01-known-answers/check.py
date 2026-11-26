#!/usr/bin/env python3
"""Holds a run of the known-answers kit to what the Smt tests pin and EXPECTED.md says must be.

    python -I check.py <run folder>

The run folder is the one `smt-live run` wrote (results.jsonl, report.json, integrity.json,
provenance.json). Held:

- the run is of the kit's set (its SHA-256, which make_set.py holds the set to), with the module
  the toolchain's pins.env pins, under the options of SCRIPT.md (OPTIONS);
- every one of the 469 problems of the list (AI/OutWit.Controller.AI.Smt.Tests/Slice/
  fetch-list.tsv) has its row, answered (ok, or output_exceeded: the model is more than a
  session may write, the verdict stands), with the list's verdict, and no row is of a problem
  the list does not name;
- the statuses and the agreement with the expected verdicts are EXPECTED.md's (STATUSES,
  EXPECTATIONS);
- the evaluator's classes of the satisfiable problems' models, by logic, are the ones the
  slice test of the solver pins (the table CLASSES of Solver/SmtSolverSliceTests.cs, read
  from that file), and none is failed;
- every problem was answered by two machines (--rate 1): the sampling as asked, every row
  settled by its copies;
- the integrity document says, and says 0 for, the lies, the disagreements between the
  evaluator and the solver, the problems without a result and the standing disputes.

Prints the table by logic and the run's totals (fuel, resource counts, copies). Exits 1 when
anything is not held.
"""

import importlib.util
import json
import os
import re
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
PROBLEMS = 469
ANSWERED = ("ok", "output_exceeded")
OPTIONS = {"query": "check", "rlimit": 2000000000, "hard_cap_s": 100, "memory_mb": 1024, "seed": 0, "sampling_rate": 1}
STATUSES = {"ok": 468, "output_exceeded": 1}
EXPECTATIONS = {"agree": 469}
SETTLED_BY = {"copies": 469}
CLEAN = ("lies", "evaluator_solver_disagreements", "no_result", "disputed")


def ai_folder():
    return os.path.dirname(os.path.dirname(os.path.dirname(HERE)))


def tests_folder():
    return os.path.join(ai_folder(), "OutWit.Controller.AI.Smt.Tests")


def load_make_set():
    spec = importlib.util.spec_from_file_location("make_set", os.path.join(HERE, "make_set.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


SET_SHA256 = load_make_set().SET_SHA256


def module_sha256():
    """The module the toolchain's pins.env pins: the one every run of this controller solves with."""
    path = os.path.join(ai_folder(), "OutWit.Controller.AI.Smt.Assets", "toolchain", "pins.env")
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            if line.startswith("Z3_MODULE_SHA256="):
                return line.strip().split("=", 1)[1]
    raise SystemExit("no Z3_MODULE_SHA256 in %s" % path)


def read_list():
    path = os.path.join(tests_folder(), "Slice", "fetch-list.tsv")
    with open(path, encoding="utf-8") as handle:
        header = handle.readline().rstrip("\r\n").split("\t")
        return [dict(zip(header, line.rstrip("\r\n").split("\t"))) for line in handle if line.strip()]


def read_classes():
    path = os.path.join(tests_folder(), "Solver", "SmtSolverSliceTests.cs")
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
    table = text[text.index("CLASSES"):]
    table = table[:table.index("};")]
    classes = {logic: (int(a), int(b), int(c)) for logic, a, b, c in re.findall(r'\["(\w+)"\]\s*=\s*\((\d+),\s*(\d+),\s*(\d+)\)', table)}
    if not classes:
        raise SystemExit("the table CLASSES was not found in %s" % path)
    return classes


def read_rows(folder):
    rows = {}
    with open(os.path.join(folder, "results.jsonl"), encoding="utf-8") as handle:
        for line in handle:
            if line.strip():
                row = json.loads(line)
                rows[row["id"]] = row
    return rows


def read_json(folder, name):
    with open(os.path.join(folder, name), encoding="utf-8") as handle:
        return json.load(handle)


def counts_differ(name, seen, want):
    """A fault when the counts seen are not the page's; every word either names is compared."""
    words = sorted(set(seen) | set(want))
    if all(seen.get(word, 0) == want.get(word, 0) for word in words):
        return None
    return "%s: %s; EXPECTED.md: %s" % (name, ", ".join("%s %d" % (word, seen[word]) for word in sorted(seen) if seen[word]),
                                       ", ".join("%s %d" % (word, want[word]) for word in sorted(want) if want[word]))


def hold(listing, pinned, rows, report, integrity, provenance):
    """The faults of a run against the list and the page, and the lines that describe it."""
    faults = []
    lines = []
    if len(listing) != PROBLEMS:
        faults.append("the list names %d problems, not %d" % (len(listing), PROBLEMS))

    run = report.get("run", {})
    options = run.get("options", {})
    for name, value in OPTIONS.items():
        if options.get(name) != value:
            faults.append("the run's %s is %s, the kit's %s" % (name, options.get(name), value))
    if provenance.get("problem_set", {}).get("sha256") != SET_SHA256:
        faults.append("the run is of the set %s, not the kit's %s" % (provenance.get("problem_set", {}).get("sha256"), SET_SHA256))
    if provenance.get("profile", {}).get("module_sha256") != module_sha256():
        faults.append("the run's module is %s, not the pinned %s" % (provenance.get("profile", {}).get("module_sha256"), module_sha256()))

    listed = {item["id"] for item in listing}
    strays = sorted(name for name in rows if name not in listed)
    if strays:
        faults.append("%d row(s) of the results are not of the list (%s)" % (len(strays), ", ".join(strays[:10])))

    by_logic = defaultdict(Counter)
    classes = defaultdict(lambda: [0, 0, 0])
    for item in listing:
        row = rows.get(item["id"])
        logic = item["logic"]
        by_logic[logic]["problems"] += 1
        if row is None:
            faults.append("%s: no row" % item["id"])
            continue
        if row["status"] not in ANSWERED:
            faults.append("%s: %s %s %s" % (item["id"], row["status"], row.get("code", ""), row.get("message", "")[:80]))
        if row["verdict"] != item["status"]:
            faults.append("%s (%s): verdict %s, the list says %s" % (item["id"], logic, row["verdict"], item["status"]))
        by_logic[logic][row["verdict"]] += 1
        if row["verdict"] == "sat":
            position = {"validated": 0, "partial": 1, "failed": 2}.get(row["validation"])
            if position is None:
                faults.append("%s: a sat row whose validation is %s" % (item["id"], row["validation"]))
            else:
                classes[logic][position] += 1
            if row["validation"] == "failed":
                faults.append("%s (%s): the evaluator calls the model failed: %s %s" % (item["id"], logic, row["validation_reason"], row.get("validation_detail", "")))

    answered = [rows[item["id"]] for item in listing if item["id"] in rows]
    for fault in (counts_differ("statuses", Counter(row["status"] for row in answered), STATUSES),
                  counts_differ("expected", Counter(row["expected_agreement"] for row in answered), EXPECTATIONS),
                  counts_differ("settled by", Counter(row["settled_by"] for row in answered), SETTLED_BY)):
        if fault:
            faults.append(fault)
    if run.get("sampling") != "as_asked":
        faults.append("the sampling is %s, not as_asked" % run.get("sampling"))

    logics = sorted(set(by_logic) | set(pinned))
    lines.append("%-9s %8s %5s %6s %6s   %-26s %s" % ("logic", "problems", "sat", "unsat", "other", "validated/partial/failed", "pinned"))
    for logic in logics:
        counts = by_logic[logic]
        other = counts["problems"] - counts["sat"] - counts["unsat"]
        seen = tuple(classes[logic])
        want = pinned.get(logic, (0, 0, 0))
        mark = "" if seen == want else "  DIFFERS"
        lines.append("%-9s %8d %5d %6d %6d   %-26s %s%s" % (logic, counts["problems"], counts["sat"], counts["unsat"], other, "%d/%d/%d" % seen, "%d/%d/%d" % want, mark))
        if seen != want:
            faults.append("%s: the evaluator's classes are %d/%d/%d, the slice test pins %d/%d/%d" % ((logic,) + seen + want))

    # A count the integrity document does not give is not a 0.
    for name in CLEAN:
        if name not in integrity:
            faults.append("integrity: no count of %s" % name)
        elif integrity[name] != 0:
            faults.append("integrity: %s %d" % (name, integrity[name]))

    taken = [rows[item["id"]] for item in listing if item["id"] in rows and "stats" in rows[item["id"]]]
    if taken:
        heaviest = max(taken, key=lambda row: row["stats"]["fuel"])
        lines.append("fuel     : %d in all, the most %d (%s)" % (sum(row["stats"]["fuel"] for row in taken), heaviest["stats"]["fuel"], heaviest["id"]))
        lines.append("resource : %d in all" % sum(row["stats"]["resource_count"] for row in taken))
    lines.append("copies   : %d problem(s) compared, %d agreement(s); settled by %s" % (
        integrity.get("compared", 0), integrity.get("agreements", 0),
        ", ".join("%s %d" % pair for pair in sorted(Counter(row["settled_by"] for row in rows.values()).items()))))
    lines.append("integrity: lies %s, evaluator/solver disagreements %s, no result %s, disputes %s (standing %s), repairs %s, node faults %s" % tuple(
        integrity.get(name, "-") for name in ("lies", "evaluator_solver_disagreements", "no_result", "disputes", "disputed", "repairs", "node_faults")))
    return faults, lines


def main(argv):
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 64
    folder = argv[1]
    faults, lines = hold(read_list(), read_classes(), read_rows(folder), read_json(folder, "report.json"),
                         read_json(folder, "integrity.json")["summary"], read_json(folder, "provenance.json"))
    for line in lines:
        print(line)
    for fault in faults[:40]:
        print("NOT HELD: %s" % fault)
    if len(faults) > 40:
        print("NOT HELD: and %d more" % (len(faults) - 40))
    print("held: everything" if not faults else "held: NO, %d fault(s)" % len(faults))
    return 0 if not faults else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
