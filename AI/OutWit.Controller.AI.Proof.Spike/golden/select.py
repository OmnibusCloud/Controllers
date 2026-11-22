"""PF-0 spike: choosing the golden set (run once, on one runner; its list is committed).

    select.py --parquet <file> --lean-root <toolchain root> --work <dir> --out <dir>
              [--seed N] [--jobs N] [--model N] [--human N] [--with-sorry N]

From the pinned NuminaMath-LEAN file: every row that carries a proof (the model's
`formal_proof`, or a human `formal_ground_truth` marked complete or with_sorry) and that has
the shape of a `check` item (golden/items.py) is a candidate. A seeded sample of the candidates
is checked on this runner with the profile of the gate; from the outcome the set is chosen:
accepted items, the rejections that came by themselves (proofs the current Mathlib no longer
takes, unfinished proofs, statements that no longer elaborate), and made rejections derived
from accepted items by the rules of golden/items.py, which are then checked too. Written:

    list.tsv            the set: ids, rules, dataset ids, SHA-256 of each text, licence, and
                        what this runner said (no text of the dataset)
    select-report.md    the counts: shapes, the sample's verdicts by source, the set
    sample-rows.jsonl   the sample's result rows
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import random
import subprocess
import sys

import items as golden

HERE = os.path.dirname(os.path.abspath(__file__))
SPIKE = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(SPIKE, "harness"))
from pf0lib import EXE, read_jsonl  # noqa: E402

FIELDS = {"k": "formal_proof", "h": "formal_ground_truth", "w": "formal_ground_truth"}
COLUMNS = ["id", "class", "rule", "base", "other", "param", "uuid", "field", "challenge_sha256",
           "solution_sha256", "licence", "expect", "heartbeats"]


def load_rows(parquet: str) -> list:
    import pyarrow.parquet as pq
    table = pq.read_table(parquet, columns=["uuid", "author", "formal_statement", "formal_ground_truth",
                                            "ground_truth_type", "formal_proof", "problem_type", "source"])
    return table.to_pylist()


def candidates(rows: list) -> tuple:
    """Candidates by source kind (k: model proof, h: complete human proof, w: human proof with
    sorry) and the count of every reason a row with a proof is not one."""
    found = {"k": [], "h": [], "w": []}
    reasons = {"k": collections.Counter(), "h": collections.Counter(), "w": collections.Counter()}
    for row in rows:
        kinds = []
        if row.get("formal_proof"):
            kinds.append("k")
        if row.get("formal_ground_truth") and row.get("ground_truth_type") == "complete":
            kinds.append("h")
        if row.get("formal_ground_truth") and row.get("ground_truth_type") == "with_sorry":
            kinds.append("w")
        for kind in kinds:
            item, reason = golden.derive(row["formal_statement"], row[FIELDS[kind]])
            reasons[kind][reason or "usable"] += 1
            if item:
                item.update({"id": f"n-{kind}-{row['uuid'][:13]}", "uuid": row["uuid"], "field": FIELDS[kind],
                             "kind": kind, "problem_type": row.get("problem_type") or ""})
                found[kind].append(item)
    return found, reasons


def run_check(options: argparse.Namespace, entries: list, name: str) -> dict:
    path = os.path.join(options.out, name + "-items.jsonl")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        for entry in entries:
            handle.write(json.dumps(entry, sort_keys=True) + "\n")
    rows_path = os.path.join(options.out, name + "-rows.jsonl")
    lean = os.path.join(options.lean_root, "bin", "lean" + EXE)
    subprocess.run([sys.executable, os.path.join(SPIKE, "harness", "check.py"), "--lean", lean,
                    "--lean-path", os.path.join(options.work, "prelude"),
                    "--lean-path-file", os.path.join(options.work, "leanpath.json"), "--import", "Mathlib",
                    "--items", path, "--out", rows_path, "--work", os.path.join(options.work, "select-" + name),
                    "--jobs", str(options.jobs), "--max-heartbeats", str(options.max_heartbeats),
                    "--sample", "0"], check=True)
    os.remove(path)
    return {row["id"]: row for row in read_jsonl(rows_path)}


def outcome(row: dict) -> str:
    return row.get("verdict") or row.get("status") or "none"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--parquet", required=True)
    parser.add_argument("--lean-root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--seed", type=int, default=20261122)
    parser.add_argument("--jobs", type=int, default=4)
    parser.add_argument("--model", type=int, default=900)
    parser.add_argument("--human", type=int, default=320)
    parser.add_argument("--with-sorry", type=int, default=40)
    parser.add_argument("--max-heartbeats", type=int, default=400000)
    options = parser.parse_args()
    os.makedirs(options.out, exist_ok=True)
    rng = random.Random(options.seed)

    rows = load_rows(options.parquet)
    found, reasons = candidates(rows)
    sample = []
    for kind, count in (("k", options.model), ("h", options.human), ("w", options.with_sorry)):
        pool = sorted(found[kind], key=lambda item: item["id"])
        sample += rng.sample(pool, min(count, len(pool)))
    ids = collections.Counter(item["id"] for item in sample)
    sample = [item for item in sample if ids[item["id"]] == 1]
    by_id = {item["id"]: item for item in sample}
    results = run_check(options, sample, "sample")

    verdicts = {kind: collections.Counter() for kind in "khw"}
    for item in sample:
        verdicts[item["kind"]][outcome(results.get(item["id"], {}))] += 1

    def pick(pool: list, count: int) -> list:
        pool = sorted(pool)
        return sorted(rng.sample(pool, min(count, len(pool))))

    accepted = [i for i in by_id if outcome(results.get(i, {})) == "accepted" and results[i].get("heartbeats")]
    heavy = sorted(accepted, key=lambda i: -results[i]["heartbeats"])[:30]
    human = pick([i for i in accepted if by_id[i]["kind"] == "h" and i not in heavy], 80)
    rest = pick([i for i in accepted if i not in heavy and i not in human], 300 - len(heavy) - len(human))
    chosen_accepted = sorted(set(heavy) | set(human) | set(rest))

    natural = []
    for wanted, count in (("rejected", 150), ("budget", 25), ("incomplete", 25), ("statement_mismatch", 10),
                          ("challenge_error", 15), ("timeout", 5), ("axioms_refused", 10)):
        natural += pick([i for i in by_id if outcome(results.get(i, {})) == wanted], count)

    listed = []

    def add(entry_id: str, cls: str, item: dict, rule: str = "", base: str = "", other: str = "", param: str = "",
            expect: str = "", heartbeats=None) -> None:
        listed.append({
            "id": entry_id, "class": cls, "rule": rule, "base": base, "other": other, "param": param,
            "uuid": item.get("uuid", ""), "field": item.get("field", ""),
            "challenge_sha256": golden.sha(item["challenge"]),
            "solution_sha256": golden.sha("\n".join(item.get("solution_pre", []) + [item["solution"]])),
            "licence": golden.LICENCE if item.get("uuid") or base else "own", "expect": expect,
            "heartbeats": "" if heartbeats is None else str(heartbeats), "_item": item})

    for i in chosen_accepted:
        add(i, "accepted", by_id[i], expect="accepted", heartbeats=results[i].get("heartbeats"))
    for i in natural:
        add(i, "natural", by_id[i], expect=outcome(results[i]), heartbeats=results[i].get("heartbeats"))

    # Made rejections, each from an accepted item of the set.
    made = []

    def make(rule: str, bases: list, others: dict | None = None, params: dict | None = None) -> None:
        for base in bases:
            other = (others or {}).get(base, "")
            param = (params or {}).get(base, "")
            item = golden.made(rule if rule not in ("budget_low", "budget_edge") else "budget",
                               by_id[base], by_id.get(other), param)
            if item is None:
                continue
            item["id"] = f"made-{rule}-{base[2:]}"
            made.append((item, rule, base, other, param))

    by_opens = collections.defaultdict(list)
    for i in chosen_accepted:
        by_opens[tuple(by_id[i]["opens"])].append(i)
    partner = {}
    for group in by_opens.values():
        for index, i in enumerate(group):
            if len(group) > 1:
                partner[i] = group[(index + 1) % len(group)]
    light = [i for i in chosen_accepted if i not in heavy]
    make("sorry", pick(light, 15))
    make("mismatch", pick([i for i in light if i in partner], 24), others=partner)
    make("drop_last", pick(light, 48))
    make("axiom", pick(light, 10))
    low = pick(chosen_accepted, 15)
    make("budget_low", low, params={i: str(max(1, results[i]["heartbeats"] // 2000)) for i in low})
    edge = heavy[:20]
    make("budget_edge", edge, params={i: str(results[i]["heartbeats"] // 1000 + 1) for i in edge})
    make("memout", pick(light, 2), params={i: "1024" for i in light})
    authored = golden.authored()
    made_items = [item for item, _rule, _base, _other, _param in made] + authored
    made_results = run_check(options, made_items, "made")
    for item, rule, base, other, param in made:
        row = made_results.get(item["id"], {})
        add(item["id"], "made", item, rule=rule, base=base, other=other, param=param,
            expect=outcome(row), heartbeats=row.get("heartbeats"))
    for item in authored:
        row = made_results.get(item["id"], {})
        add(item["id"], "made", item, rule=item["rule"], expect=outcome(row), heartbeats=row.get("heartbeats"))

    with open(os.path.join(options.out, "list.tsv"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\t".join(COLUMNS) + "\n")
        for entry in listed:
            handle.write("\t".join(str(entry[column]) for column in COLUMNS) + "\n")

    lines = ["### Golden set selection", "",
             f"Source: {golden.SOURCE} ({golden.LICENCE}), {len(rows)} rows; seed {options.seed}; "
             f"budget {options.max_heartbeats} thousand heartbeats.", "",
             "| rows with | usable as a `check` item | not usable, by reason |", "|---|---|---|"]
    names = {"k": "a model proof", "h": "a complete human proof", "w": "a human proof with sorry"}
    for kind in "khw":
        other = ", ".join(f"{reason} {count}" for reason, count in reasons[kind].most_common() if reason != "usable")
        lines.append(f"| {names[kind]} | {reasons[kind]['usable']} of {sum(reasons[kind].values())} | {other} |")
    lines += ["", "| sample of | checked | verdicts on this profile |", "|---|---|---|"]
    for kind in "khw":
        total = sum(verdicts[kind].values())
        text = ", ".join(f"{verdict} {count}" for verdict, count in verdicts[kind].most_common())
        lines.append(f"| {names[kind]} | {total} | {text} |")
    expect = collections.Counter((entry["class"], entry["rule"], entry["expect"]) for entry in listed)
    lines += ["", f"The set: {len(listed)} items.", "", "| class | rule | this runner says | items |", "|---|---|---|---|"]
    for (cls, rule, verdict), count in sorted(expect.items()):
        lines.append(f"| {cls} | {rule} | {verdict} | {count} |")
    report = "\n".join(lines) + "\n"
    with open(os.path.join(options.out, "select-report.md"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(report)
    print(report)
    return 0


if __name__ == "__main__":
    sys.exit(main())
