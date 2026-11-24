#!/usr/bin/env python3
"""Holds a run of the long-tail kit to what must be so on any fleet.

    python -I check.py <run folder> [--list <kit-list.tsv>]

The run folder is the one `smt-live run` wrote (results.jsonl, report.json, integrity.json,
provenance.json) for the set make_set.py packed, under the options of SCRIPT.md. Held:

- the run's options are the kit's (OPTIONS): the numbers below are about them;
- every problem of the list (kit-list.tsv beside this script) has its row, answered (ok, or
  output_exceeded: the model is more than a session may write, the verdict stands);
- each verdict is the problem's :status, or unknown because a budget ended (the resource limit,
  the fuel of the hard cap, the memory): never the opposite verdict, never unknown for any other
  reason;
- the evaluator calls no model failed, and it validates at least VALIDATED_SHARE of the sat rows;
- the integrity document finds no lie, no disagreement between the evaluator and the solver, no
  problem without a result and no standing dispute.

Prints the verdicts by logic and by time band of the competition, the unknowns by reason, the
evaluator's classes, the copies, the cost of the long tail (fuel, as seconds of the reference
machine, against the competition's time) and the heaviest problems: the numbers EXPECTED.md
leaves to be filled in after a real run. Exits 1 when anything is not held.
"""

import json
import os
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
ANSWERED = ("ok", "output_exceeded")
BUDGET_ENDS = ("rlimit", "fuel", "memout")
VALIDATED_SHARE = 0.95
FUEL_PER_SECOND = 5_000_000_000
OPTIONS = {"query": "check", "rlimit": 4294967295, "hard_cap_s": 300, "memory_mb": 1024}
BANDS = ("1-2", "2-5", "5-10", "10-20", "20-40", "40-60")
HEAVIEST = 10


def read_list(path):
    with open(path, encoding="utf-8") as handle:
        header = handle.readline().rstrip("\r\n").split("\t")
        return [dict(zip(header, line.rstrip("\r\n").split("\t"))) for line in handle if line.strip()]


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


def percentile(values, share):
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(share * len(ordered)))]


def hold(listing, rows, report, integrity):
    """The faults of a run against the list, and the lines that describe it."""
    faults = []
    lines = []

    options = report.get("run", {}).get("options", {})
    for name, value in OPTIONS.items():
        if options.get(name) != value:
            faults.append("the run's %s is %s, the kit's %s" % (name, options.get(name), value))

    by_logic = defaultdict(Counter)
    by_band = defaultdict(Counter)
    unknown = Counter()
    classes = defaultdict(Counter)
    seconds = []
    for item in listing:
        logic = item["logic"]
        by_logic[logic]["problems"] += 1
        row = rows.get(item["id"])
        if row is None:
            faults.append("%s: no row" % item["id"])
            continue
        if row["status"] not in ANSWERED:
            faults.append("%s: %s %s %s" % (item["id"], row["status"], row.get("code", ""), row.get("message", "")[:80]))
            continue
        verdict = row["verdict"]
        by_logic[logic][verdict] += 1
        by_band[item["band"]][verdict if verdict == "unknown" else "decided"] += 1
        if verdict == "unknown":
            unknown[row["reason"]] += 1
            if row["reason"] not in BUDGET_ENDS:
                faults.append("%s (%s): unknown for %s, which is no budget's end" % (item["id"], logic, row["reason"]))
        elif verdict != item["status"]:
            faults.append("%s (%s): verdict %s, the problem's :status says %s" % (item["id"], logic, verdict, item["status"]))
        if verdict == "sat":
            classes[logic][row["validation"]] += 1
            if row["validation"] == "failed":
                faults.append("%s (%s): the evaluator calls the model failed: %s %s" % (
                    item["id"], logic, row.get("validation_reason", ""), row.get("validation_detail", "")))
        stats = row.get("stats")
        if stats and verdict != "unknown":
            seconds.append((stats["fuel"] / FUEL_PER_SECOND, float(item["time_s"]), item))

    sat = sum(counts["sat"] for counts in by_logic.values())
    validated = sum(counts["validated"] for counts in classes.values())
    if sat and validated < VALIDATED_SHARE * sat:
        faults.append("the evaluator validated %d of %d sat rows, less than %d %%" % (validated, sat, round(VALIDATED_SHARE * 100)))

    for name in ("lies", "evaluator_solver_disagreements", "no_result", "disputed"):
        if integrity.get(name, 0) != 0:
            faults.append("integrity: %s %d" % (name, integrity[name]))

    lines.append("%-7s %8s %5s %6s %8s   %s" % ("logic", "problems", "sat", "unsat", "unknown", "validated/partial/failed"))
    for logic in sorted(by_logic):
        counts = by_logic[logic]
        lines.append("%-7s %8d %5d %6d %8d   %d/%d/%d" % (
            logic, counts["problems"], counts["sat"], counts["unsat"], counts["unknown"],
            classes[logic]["validated"], classes[logic]["partial"], classes[logic]["failed"]))
    lines.append("bands    : %s (the competition's seconds: decided/unknown)" % ", ".join(
        "%s %d/%d" % (band, by_band[band]["decided"], by_band[band]["unknown"]) for band in BANDS))
    lines.append("unknown  : %d, %s" % (sum(unknown.values()), ", ".join("%s %d" % pair for pair in sorted(unknown.items())) or "none"))
    lines.append("validated: %d of %d sat rows (%.1f %%)" % (validated, sat, 100.0 * validated / sat if sat else 100.0))
    if seconds:
        ratios = [fuel / time for fuel, time, _ in seconds]
        lines.append("decided  : %d; reference seconds (fuel / %d) median %.1f, p90 %.1f, the most %.1f; per competition second median %.2f, p10 %.2f, p90 %.2f" % (
            len(seconds), FUEL_PER_SECOND, percentile([s for s, _, _ in seconds], 0.5), percentile([s for s, _, _ in seconds], 0.9),
            max(s for s, _, _ in seconds), percentile(ratios, 0.5), percentile(ratios, 0.1), percentile(ratios, 0.9)))
    tail = sorted(((row["stats"]["fuel"], item["id"], row["verdict"], item["time_s"]) for item in listing
                   for row in [rows.get(item["id"])] if row and row.get("stats")), reverse=True)
    if tail:
        lines.append("fuel     : %d in all" % sum(entry[0] for entry in tail))
        lines.append("heaviest : %s" % ", ".join("%s %s %.0f s (%s s at the competition)" % (
            entry[1], entry[2], entry[0] / FUEL_PER_SECOND, entry[3]) for entry in tail[:HEAVIEST]))
    lines.append("copies   : %d problem(s) compared, %d agreement(s); unsat on one machine %d; settled by %s" % (
        integrity.get("compared", 0), integrity.get("agreements", 0), integrity.get("unsat_one_copy", 0),
        ", ".join("%s %d" % pair for pair in sorted(Counter(row["settled_by"] for row in rows.values()).items()))))
    lines.append("integrity: lies %d, evaluator/solver disagreements %d, no result %d, disputes %d (standing %d), repairs %d, node faults %d" % (
        integrity.get("lies", 0), integrity.get("evaluator_solver_disagreements", 0), integrity.get("no_result", 0),
        integrity.get("disputes", 0), integrity.get("disputed", 0), integrity.get("repairs", 0), integrity.get("node_faults", 0)))
    return faults, lines


def main(argv):
    args = argv[1:]
    listing = os.path.join(HERE, "kit-list.tsv")
    if "--list" in args:
        at = args.index("--list")
        if at + 1 >= len(args):
            print(__doc__, file=sys.stderr)
            return 64
        listing = args[at + 1]
        del args[at:at + 2]
    if len(args) != 1:
        print(__doc__, file=sys.stderr)
        return 64
    folder = args[0]

    faults, lines = hold(read_list(listing), read_rows(folder), read_json(folder, "report.json"),
                         read_json(folder, "integrity.json")["summary"])
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
