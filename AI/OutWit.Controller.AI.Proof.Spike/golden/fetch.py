"""PF-0 spike: the golden set's texts, rebuilt from the pinned dataset file by the committed list.

    fetch.py --parquet <file> --list <list.tsv> --out <items.jsonl> [--attribution <file>]

Every listed item is derived again (golden/items.py: a dataset row, a made rule on a listed
item, or an item written for the gate) and both of its texts are compared with the SHA-256 the
list carries; a difference fails the fetch. The texts go to the output file only (an artifact
of the run); they are never committed.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

import items as golden


def read_list(path: str) -> list:
    with open(path, encoding="utf-8") as handle:
        header = handle.readline().rstrip("\n").split("\t")
        return [dict(zip(header, line.rstrip("\n").split("\t"))) for line in handle if line.strip()]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--parquet", required=True)
    parser.add_argument("--list", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--attribution", default="")
    options = parser.parse_args()
    listed = read_list(options.list)
    wanted = {entry["uuid"] for entry in listed if entry["uuid"]}

    import pyarrow.parquet as pq
    table = pq.read_table(options.parquet, columns=["uuid", "formal_statement", "formal_ground_truth", "formal_proof"])
    rows = {row["uuid"]: row for row in table.to_pylist() if row["uuid"] in wanted}

    authored = {item["id"]: item for item in golden.authored()}
    built = {}
    for entry in listed:
        if entry["class"] in ("accepted", "natural"):
            row = rows.get(entry["uuid"])
            if row is None:
                print(f"{entry['id']}: the dataset has no row {entry['uuid']}", file=sys.stderr)
                return 1
            item, reason = golden.derive(row["formal_statement"], row[entry["field"]])
            if item is None:
                print(f"{entry['id']}: not derivable ({reason})", file=sys.stderr)
                return 1
            built[entry["id"]] = item
    bad = 0
    out = []
    for entry in listed:
        if entry["id"] in built:
            item = dict(built[entry["id"]])
        elif entry["id"] in authored:
            item = dict(authored[entry["id"]])
        else:
            rule = "budget" if entry["rule"] in ("budget_low", "budget_edge") else entry["rule"]
            item = golden.made(rule, built[entry["base"]], built.get(entry["other"]), entry["param"])
            if item is None:
                print(f"{entry['id']}: the rule {entry['rule']} does not apply", file=sys.stderr)
                return 1
        solution = "\n".join(item.get("solution_pre", []) + [item["solution"]])
        if golden.sha(item["challenge"]) != entry["challenge_sha256"] or golden.sha(solution) != entry["solution_sha256"]:
            print(f"HASH MISMATCH {entry['id']}", file=sys.stderr)
            bad += 1
            continue
        item.pop("head", None)
        item.pop("rule", None)
        item.update({"id": entry["id"], "class": entry["class"], "made": entry["rule"], "expect": entry["expect"]})
        out.append(item)
    if bad:
        return 1
    os.makedirs(os.path.dirname(os.path.abspath(options.out)), exist_ok=True)
    with open(options.out, "w", encoding="utf-8", newline="\n") as handle:
        for item in out:
            handle.write(json.dumps(item, sort_keys=True) + "\n")
    if options.attribution:
        with open(options.attribution, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(f"The items of this file are taken or derived from {golden.SOURCE} "
                         f"(https://huggingface.co/datasets/{golden.SOURCE}), licence {golden.LICENCE}; "
                         "items whose licence column says `own` were written for this gate.\n")
            handle.write("id\tdataset row\tfield\tlicence\n")
            for entry in listed:
                handle.write(f"{entry['id']}\t{entry['uuid'] or entry['base']}\t{entry['field']}\t{entry['licence']}\n")
    print(f"{len(out)} items, every text matches its SHA-256")
    return 0


if __name__ == "__main__":
    sys.exit(main())
