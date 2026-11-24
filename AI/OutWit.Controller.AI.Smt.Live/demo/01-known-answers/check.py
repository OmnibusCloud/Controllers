#!/usr/bin/env python3
"""Holds a run of the known-answers kit to what the Smt tests pin.

    python -I check.py <run folder>

The run folder is the one `smt-live run` wrote (results.jsonl, report.json, integrity.json,
provenance.json). Held:

- every one of the 469 problems of the list (AI/OutWit.Controller.AI.Smt.Tests/Slice/
  fetch-list.tsv) has its row, answered (ok, or output_exceeded: the model is more than a
  session may write, the verdict stands), with the list's verdict;
- the evaluator's classes of the satisfiable problems' models, by logic, are the ones the
  slice test of the solver pins (the table CLASSES of Solver/SmtSolverSliceTests.cs, read
  from that file), and none is failed;
- the integrity document finds no lie, no disagreement between the evaluator and the
  solver, no problem without a result and no standing dispute.

Prints the table by logic and the run's totals (fuel, resource counts, copies), the numbers
EXPECTED.md leaves to be filled in after a real run. Exits 1 when anything is not held.
"""

import json
import os
import re
import sys
from collections import Counter, defaultdict

PROBLEMS = 469
ANSWERED = ("ok", "output_exceeded")


def tests_folder():
    tool = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    return os.path.join(os.path.dirname(tool), "OutWit.Controller.AI.Smt.Tests")


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


def main(argv):
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 64
    folder = argv[1]
    listing = read_list()
    pinned = read_classes()
    rows = read_rows(folder)
    with open(os.path.join(folder, "integrity.json"), encoding="utf-8") as handle:
        integrity = json.load(handle)["summary"]

    faults = []
    if len(listing) != PROBLEMS:
        faults.append("the list names %d problems, not %d" % (len(listing), PROBLEMS))

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
            faults.append("%s: %s %s %s" % (item["id"], row["status"], row["code"], row["message"][:80]))
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

    logics = sorted(set(by_logic) | set(pinned))
    print("%-9s %8s %5s %6s %6s   %-26s %s" % ("logic", "problems", "sat", "unsat", "other", "validated/partial/failed", "pinned"))
    for logic in logics:
        counts = by_logic[logic]
        other = counts["problems"] - counts["sat"] - counts["unsat"]
        seen = tuple(classes[logic])
        want = pinned.get(logic, (0, 0, 0))
        mark = "" if seen == want else "  DIFFERS"
        print("%-9s %8d %5d %6d %6d   %-26s %s%s" % (logic, counts["problems"], counts["sat"], counts["unsat"], other, "%d/%d/%d" % seen, "%d/%d/%d" % want, mark))
        if seen != want:
            faults.append("%s: the evaluator's classes are %d/%d/%d, the slice test pins %d/%d/%d" % ((logic,) + seen + want))

    for name in ("lies", "evaluator_solver_disagreements", "no_result", "disputed"):
        if integrity.get(name, 0) != 0:
            faults.append("integrity: %s %d" % (name, integrity[name]))

    taken = [rows[item["id"]] for item in listing if item["id"] in rows and "stats" in rows[item["id"]]]
    if taken:
        heaviest = max(taken, key=lambda row: row["stats"]["fuel"])
        print("fuel     : %d in all, the most %d (%s)" % (sum(row["stats"]["fuel"] for row in taken), heaviest["stats"]["fuel"], heaviest["id"]))
        print("resource : %d in all" % sum(row["stats"]["resource_count"] for row in taken))
    print("copies   : %d problem(s) compared, %d agreement(s); settled by %s" % (
        integrity.get("compared", 0), integrity.get("agreements", 0),
        ", ".join("%s %d" % pair for pair in sorted(Counter(row["settled_by"] for row in rows.values()).items()))))
    print("integrity: lies %d, evaluator/solver disagreements %d, no result %d, disputes %d (standing %d), repairs %d, node faults %d" % (
        integrity.get("lies", 0), integrity.get("evaluator_solver_disagreements", 0), integrity.get("no_result", 0),
        integrity.get("disputes", 0), integrity.get("disputed", 0), integrity.get("repairs", 0), integrity.get("node_faults", 0)))

    for fault in faults[:40]:
        print("NOT HELD: %s" % fault)
    if len(faults) > 40:
        print("NOT HELD: and %d more" % (len(faults) - 40))
    print("held: everything" if not faults else "held: NO, %d fault(s)" % len(faults))
    return 0 if not faults else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
