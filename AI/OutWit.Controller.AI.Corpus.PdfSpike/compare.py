"""Compares the PDF spike's runs on three operating systems.

python3 -I compare.py <artifacts-dir>
<artifacts-dir>/pdf-spike-<os>/<set>-<engine>.jsonl, one JSON record per document.
Prints Markdown: per set and engine, how many documents give the same text bytes everywhere
(as produced, and with line ends normalized), then every document that differs, then the
hostile set's verdict, time and peak memory per operating system.
"""
import json
import os
import sys


def load(path):
    rows = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.strip():
                r = json.loads(line)
                rows[r["File"]] = r
    return rows


def main():
    root = sys.argv[1]
    systems = sorted(d for d in os.listdir(root) if d.startswith("pdf-spike-"))
    runs = {}
    for s in systems:
        for name in os.listdir(os.path.join(root, s)):
            if name.endswith(".jsonl"):
                runs.setdefault(name[:-6], {})[s] = load(os.path.join(root, s, name))

    print("## Same text on every operating system\n")
    print("| run | documents | ok everywhere | identical bytes | identical after LF | differ |")
    print("|---|---|---|---|---|---|")
    differ_rows = []
    for run in sorted(runs):
        per = runs[run]
        if len(per) < len(systems):
            print(f"| {run} | missing on {len(systems) - len(per)} system(s) | | | | |")
            continue
        files = sorted(set().union(*[set(v) for v in per.values()]))
        ok = same = same_lf = 0
        bad = []
        for f in files:
            rs = [per[s].get(f) for s in systems]
            if any(r is None for r in rs):
                bad.append((f, "missing"))
                continue
            verdicts = {r["Verdict"] for r in rs}
            if verdicts == {"ok"}:
                ok += 1
                raw = {r["Sha256"] for r in rs}
                lf = {r["Sha256Lf"] for r in rs}
                same += len(raw) == 1
                same_lf += len(lf) == 1
                if len(lf) != 1:
                    bad.append((f, "text: " + ", ".join(f"{s[10:]}={r['Sha256Lf'][:10]} ({r['Chars']} chars)" for s, r in zip(systems, rs))))
            elif len(verdicts) != 1 or len({r.get("Exception") for r in rs}) != 1:
                bad.append((f, "verdict: " + ", ".join(f"{s[10:]}={r['Verdict']}/{r.get('Exception') or ''}" for s, r in zip(systems, rs))))
        print(f"| {run} | {len(files)} | {ok} | {same} | {same_lf} | {len(bad)} |")
        differ_rows += [(run, f, why) for f, why in bad]

    print("\n## Documents that differ between systems\n")
    if not differ_rows:
        print("none")
    for run, f, why in differ_rows:
        print(f"- `{run}` `{f}`: {why}")

    for run in sorted(r for r in runs if r.startswith("hostile-")):
        per = runs[run]
        print(f"\n## {run}: verdict, wall ms, peak MB\n")
        print("| file | " + " | ".join(s[10:] for s in systems) + " |")
        print("|---|" + "---|" * len(systems))
        files = sorted(set().union(*[set(v) for v in per.values()]))
        for f in files:
            cells = []
            for s in systems:
                r = per.get(s, {}).get(f)
                if r is None:
                    cells.append("-")
                    continue
                detail = r.get("Exception") or r.get("FirstPageError") or ""
                if r["Verdict"] == "crash":
                    detail = (r.get("Message") or "")[:40]
                cells.append(f"{r['Verdict']} {r['WallMs']:.0f} ms {r['PeakMb']:.0f} MB {r['Pages']}p {r['Chars']}c {detail}".strip())
            print(f"| {f} | " + " | ".join(cells) + " |")

    for s in systems:
        path = os.path.join(root, s, "throughput.txt")
        if os.path.exists(path):
            print(f"\n## Throughput in one process, {s[10:]}\n")
            print("```")
            print(open(path, encoding="utf-8").read().strip())
            print("```")


if __name__ == "__main__":
    main()
