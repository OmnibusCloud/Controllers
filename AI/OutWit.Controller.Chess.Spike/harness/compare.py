"""Compares the canonical records of every cell byte for byte, per configuration, and writes
summary.md and summary.json. Exit code 1 when any record differs, a record is missing where
its set should hold it, a bench signature is wrong, or an exported network differs from the
pin.

usage: python compare.py --root <folder with the cells' result folders> --out <folder>
                         --bench 2497913 --net-sha256 <sha>
"""

import argparse
import json
import os
import sys

PREFERRED_REFERENCE = ["linux-x64/main", "win-x64/main"]
PV_KEPT = 16


def load_cells(root):
    cells = {}
    for dirpath, _, files in os.walk(root):
        if "facts.json" in files:
            with open(os.path.join(dirpath, "facts.json")) as f:
                facts = json.load(f)
            metrics = {"runs": []}
            if "metrics.json" in files:
                with open(os.path.join(dirpath, "metrics.json")) as f:
                    metrics = json.load(f)
            cells[facts["cell"]] = {"dir": dirpath, "facts": facts, "metrics": metrics}
    return cells


def load_sets(cells):
    sets = {}  # config -> {"cell/variant": {id: line}}
    for name, cell in sorted(cells.items()):
        rdir = os.path.join(cell["dir"], "records")
        if not os.path.isdir(rdir):
            continue
        for variant in sorted(os.listdir(rdir)):
            for fn in sorted(os.listdir(os.path.join(rdir, variant))):
                if not fn.endswith(".jsonl"):
                    continue
                config = fn[:-len(".jsonl")]
                lines = {}
                with open(os.path.join(rdir, variant, fn), "r", encoding="ascii") as f:
                    for line in f:
                        line = line.rstrip("\n")
                        if line:
                            lines[json.loads(line)["id"]] = line
                sets.setdefault(config, {})[name + "/" + variant] = lines
    return sets


def flatten(value, path=""):
    if isinstance(value, dict):
        out = {}
        for k, v in value.items():
            out.update(flatten(v, path + "." + k if path else k))
        return out
    if isinstance(value, list):
        out = {path + "#len": len(value)}
        for i, v in enumerate(value):
            out.update(flatten(v, "%s[%d]" % (path, i)))
        return out
    return {path: value}


def classify(a, b):
    """Which fields differ, and the lowest rung of the fallback ladder the difference allows."""
    fa, fb = flatten(json.loads(a)), flatten(json.loads(b))
    paths = sorted(p for p in set(fa) | set(fb) if fa.get(p) != fb.get(p))
    fields = sorted(set(p.split("[")[-1].split(".")[-1].split("#")[0] for p in paths))
    pv_tail_only = all(_pv_index(p) is not None and _pv_index(p) >= PV_KEPT for p in paths)
    return {"paths": paths[:12], "fields": fields, "pv_tail_only": pv_tail_only}


def _pv_index(path):
    marker = "pv["
    i = path.rfind(marker)
    if i < 0:
        return 10 ** 6 if path.endswith("pv#len") else None
    j = path.index("]", i)
    return int(path[i + len(marker):j])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--bench", type=int, required=True)
    ap.add_argument("--net-sha256", required=True)
    args = ap.parse_args()

    cells = load_cells(args.root)
    sets = load_sets(cells)
    failures = []
    inputs = sorted(set(c["facts"].get("positions_file_sha256") for c in cells.values()))
    if len(inputs) > 1:
        failures.append("the cells ran different position files: %s" % inputs)
    md = ["# Chess CH-0: cross-cell comparison", ""]

    md += ["## Cells", "",
           "| cell | plan | OS | CPU | dispatched | binary SHA-256 | size | bench | net export | build s |",
           "|---|---|---|---|---|---|---|---|---|---|"]
    for name, c in sorted(cells.items()):
        f = c["facts"]
        bench_ok = f.get("bench_nodes") == args.bench
        net = f.get("exported_net_sha256", "n/a")
        net_ok = net in ("n/a", args.net_sha256)
        if not bench_ok:
            failures.append("%s: bench %s != %d" % (name, f.get("bench_nodes"), args.bench))
        if not net_ok:
            failures.append("%s: exported net %s" % (name, net))
        md.append("| %s | %s | %s | %s | %s | `%s` | %s | %s %s | %s | %s |" % (
            name, f.get("plan"), f.get("os", "")[:40], f.get("cpu"), f.get("dispatched_arch"),
            f.get("engine_sha256", "")[:16], f.get("engine_size"), f.get("bench_nodes"),
            "ok" if bench_ok else "FAIL", "ok" if net == args.net_sha256 else net,
            (f.get("build") or {}).get("build_seconds", "")))

    md += ["", "## Records", "",
           "Every set is compared with the reference set of its configuration on the ids both hold.",
           "", "| config | reference | sets | records compared | differing | verdict |",
           "|---|---|---|---|---|---|"]
    detail = []
    result = {"configs": {}, "failures": failures}
    for config in sorted(sets):
        by_set = sets[config]
        ref = next((r for r in PREFERRED_REFERENCE if r in by_set), None)
        if ref is None:
            ref = max(by_set, key=lambda k: len(by_set[k]))
        ref_lines = by_set[ref]
        compared = differing = 0
        per_set = {}
        for name, lines in sorted(by_set.items()):
            if name == ref:
                continue
            common = sorted(set(lines) & set(ref_lines))
            diffs = [i for i in common if lines[i] != ref_lines[i]]
            compared += len(common)
            differing += len(diffs)
            per_set[name] = {"compared": len(common), "differing": len(diffs),
                             "ids": diffs[:20]}
            for i in diffs[:5]:
                detail.append((config, name, i, classify(ref_lines[i], lines[i])))
            if diffs:
                failures.append("%s: %s differs from %s on %d of %d records"
                                % (config, name, ref, len(diffs), len(common)))
        result["configs"][config] = {"reference": ref, "reference_records": len(ref_lines),
                                     "sets": per_set}
        md.append("| %s | %s (%d) | %d | %d | %d | %s |" % (
            config, ref, len(ref_lines), len(by_set), compared, differing,
            "identical" if differing == 0 else "DIFFERENT"))

    md += ["", "## Sets", "", "| config | set | compared | differing |", "|---|---|---|---|"]
    for config in sorted(result["configs"]):
        for name, s in sorted(result["configs"][config]["sets"].items()):
            md.append("| %s | %s | %d | %d |" % (config, name, s["compared"], s["differing"]))

    if detail:
        md += ["", "## Differences (first five per set)", ""]
        for config, name, i, c in detail:
            md.append("- `%s` `%s` `%s`: fields %s, PV tail only: %s; %s" % (
                config, name, i, c["fields"], c["pv_tail_only"], c["paths"]))

    md += ["", "## Measurements", "",
           "| cell | variant | config | procs | fresh | searches | wall s | nps/process | nps aggregate | "
           "start s (median) | ucinewgame ms (median) | overshoot median/p99/max | below budget | errors |",
           "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for name, c in sorted(cells.items()):
        for r in c["metrics"].get("runs", []):
            o = r.get("overshoot") or {}
            st = r.get("process_start_seconds") or {}
            ng = r.get("newgame_seconds") or {}
            if r.get("errors"):
                failures.append("%s/%s/%s: %d engine errors" % (name, r["variant"], r["config"],
                                                                 len(r["errors"])))
            md.append("| %s | %s | %s | %d | %s | %d | %.1f | %s | %s | %s | %s | %s/%s/%s | %s | %d |" % (
                name, r["variant"], r["config"], r["procs"], r["fresh"], r["searches"],
                r["wall_seconds"], r["nps_per_process"], r["nps_aggregate"], st.get("median"),
                round(ng["median"] * 1000, 2) if ng else None, o.get("median"), o.get("p99"),
                o.get("max"), r.get("ended_below_budget"), len(r.get("errors", []))))

    md += ["", "## Verdict", ""]
    if failures:
        md += ["- " + f for f in failures]
    else:
        md.append("All canonical records identical across every cell and variant; every bench "
                  "signature %d; every exported network %s." % (args.bench, args.net_sha256[:16]))
    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "summary.md"), "w", newline="\n") as f:
        f.write("\n".join(md) + "\n")
    with open(os.path.join(args.out, "summary.json"), "w", newline="\n") as f:
        json.dump(result, f, indent=1, sort_keys=True)
    print("\n".join(md))
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
