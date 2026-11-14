#!/usr/bin/env python3
"""The SM-0 report: every platform's results side by side.

    report.py <results-root> <fetch-list.tsv>

<results-root> holds one folder per platform (results-<rid>/) with results-wasm.jsonl,
results-native.jsonl, limits.jsonl and eval.jsonl as the harness wrote them. Prints Markdown:
the platforms, cross-platform identity of the wasm runs (verdict, fuel, rlimit count, the
transcript's bytes), agreement with :status, wasm against native (verdicts, time, memory), the
limit cases, the model evaluator, and the fuel calibration. Exits 1 when the gate's hard
conditions fail: a sat/unsat disagreement with :status, or two platforms that differ on a wasm run.
"""

import json
import os
import statistics
import sys


def load(path):
    records = []
    if os.path.exists(path):
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if line:
                    records.append(json.loads(line))
    return records


def pct(values, q):
    if not values:
        return None
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, int(round(q * (len(ordered) - 1)))))
    return ordered[index]


def fmt(value, digits=2):
    if value is None:
        return "-"
    if isinstance(value, float):
        return ("%." + str(digits) + "f") % value
    return str(value)


def sci(value):
    return "-" if value is None else "%.3g" % value


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 64
    root, list_path = argv[1], argv[2]
    sys.stdout.reconfigure(encoding="utf-8")
    with open(list_path, encoding="utf-8") as handle:
        header = handle.readline().rstrip("\n").split("\t")
        rows = [dict(zip(header, line.rstrip("\n").split("\t"))) for line in handle if line.strip()]
    logic_of = {r["id"]: r["logic"] for r in rows}

    platforms = {}
    for name in sorted(os.listdir(root)):
        folder = os.path.join(root, name)
        if not os.path.isdir(folder):
            continue
        wasm = load(os.path.join(folder, "results-wasm.jsonl"))
        platforms[name.replace("results-", "")] = {
            "meta": next((r for r in wasm if r.get("kind") == "meta"), {}),
            "wasm": {r["id"]: r for r in wasm if r.get("engine") == "wasm"},
            "hostrun": {r["id"]: r for r in wasm if r.get("engine") == "wasm-host-run"},
            "native": {r["id"]: r for r in load(os.path.join(folder, "results-native.jsonl")) if r.get("engine") == "native"},
            "limits": [r for r in load(os.path.join(folder, "limits.jsonl")) if r.get("case")],
            "eval": load(os.path.join(folder, "eval.jsonl")),
        }
    out = []
    failed = False
    names = list(platforms)
    out.append("## Platforms\n")
    out.append("| platform | OS | cpus | explicit traps | module SHA-256 | wasmtime native | compile ms | load cached ms | wasm runs | native runs |")
    out.append("|---|---|---|---|---|---|---|---|---|---|")
    for name in names:
        p = platforms[name]
        m = p["meta"]
        out.append("| %s | %s | %s | %s | `%s` | %s | %s | %s | %d | %d |" % (
            name, m.get("os", "-"), m.get("cpus", "-"), m.get("explicit_traps", "-"), (m.get("module_sha256") or "-")[:16],
            m.get("wasmtime_native_sha256", "-")[:16] if m.get("wasmtime_native_sha256") else os.path.basename(str(m.get("wasmtime_native", "-"))),
            m.get("compile_ms", "-"), m.get("deserialize_ms", "-"), len(p["wasm"]), len(p["native"])))

    # Cross-platform identity of the wasm runs.
    out.append("\n## Wasm runs across platforms\n")
    ids = sorted({i for p in platforms.values() for i in p["wasm"]})
    fields = ["verdict", "fuel", "rlimit_count", "stdout_sha256", "end", "peak_memory"]
    same = {f: 0 for f in fields}
    differences = []
    complete = 0
    for i in ids:
        present = [platforms[n]["wasm"][i] for n in names if i in platforms[n]["wasm"]]
        if len(present) != len(names):
            continue
        complete += 1
        for f in fields:
            values = {json.dumps(r.get(f)) for r in present}
            if len(values) == 1:
                same[f] += 1
            elif len(differences) < 25:
                differences.append((i, f, {n: platforms[n]["wasm"][i].get(f) for n in names}))
    out.append("%d problems ran on all %d platforms (%s)." % (complete, len(names), ", ".join(names)))
    out.append("")
    out.append("| field | identical on every platform |")
    out.append("|---|---|")
    for f in fields:
        out.append("| %s | %d / %d |" % (f, same[f], complete))
    if differences:
        failed = True
        out.append("\nDifferences (first 25):\n")
        for i, f, values in differences:
            out.append("- %s %s: %s" % (i, f, values))

    # Verdicts against :status, per logic, on the first platform (identical elsewhere when the above holds).
    out.append("\n## Verdicts against :status (wasm)\n")
    out.append("| logic | problems | sat | unsat | unknown/none | agree | disagree |")
    out.append("|---|---|---|---|---|---|---|")
    totals = [0] * 6
    disagreements = []
    for logic in sorted({logic_of.get(i, "?") for i in ids}):
        counts = [0] * 6
        for i in ids:
            if logic_of.get(i) != logic:
                continue
            records = [platforms[n]["wasm"][i] for n in names if i in platforms[n]["wasm"]]
            r = records[0]
            counts[0] += 1
            counts[1 if r["verdict"] == "sat" else 2 if r["verdict"] == "unsat" else 3] += 1
            if any(x.get("agree") == "disagree" for x in records):
                counts[5] += 1
                disagreements.append((i, [(x.get("verdict"), x.get("expected")) for x in records]))
            elif r.get("agree") == "agree":
                counts[4] += 1
        totals = [a + b for a, b in zip(totals, counts)]
        out.append("| %s | %s |" % (logic, " | ".join(str(c) for c in counts)))
    out.append("| **all** | %s |" % " | ".join("**%d**" % c for c in totals))
    if disagreements:
        failed = True
        out.append("\nDisagreements with :status: %s" % disagreements)
    unknowns = {}
    for i in ids:
        r = platforms[names[0]]["wasm"].get(i)
        if r and r["verdict"] not in ("sat", "unsat"):
            key = "%s / %s / %s" % (r["verdict"], r.get("end"), r.get("reason"))
            unknowns[key] = unknowns.get(key, 0) + 1
    if unknowns:
        out.append("\nNot decided (verdict / end / reason): " + ", ".join("%s: %d" % kv for kv in sorted(unknowns.items())))

    # Host run (whole script as a file, fuel before instantiation) against the session.
    out.append("\n## The unchanged host (WasmHost.Run, stdin as a file) against the session\n")
    out.append("| platform | runs | same verdict as session | instantiation fuel (session) | host-run fuel minus session fuel: median, min, max |")
    out.append("|---|---|---|---|---|")
    for name in names:
        p = platforms[name]
        diffs = []
        agree = 0
        for i, h in p["hostrun"].items():
            s = p["wasm"].get(i)
            if not s:
                continue
            agree += h["verdict"] == s["verdict"]
            if isinstance(h.get("fuel"), int) and isinstance(s.get("fuel"), int):
                diffs.append(h["fuel"] - s["fuel"])
        inst = sorted({r.get("fuel_instantiation") for r in p["wasm"].values()})
        out.append("| %s | %d | %d | %s | %s, %s, %s |" % (name, len(p["hostrun"]), agree, inst[:3],
                   fmt(pct(diffs, 0.5)), fmt(min(diffs) if diffs else None), fmt(max(diffs) if diffs else None)))
    hostrun_ids = sorted(set.intersection(*[set(platforms[n]["hostrun"]) for n in names])) if names else []
    host_same = sum(1 for i in hostrun_ids if len({platforms[n]["hostrun"][i].get("fuel") for n in names}) == 1)
    out.append("\nHost-run fuel identical on every platform for %d of %d problems (the fuel there includes instantiation)." % (host_same, len(hostrun_ids)))

    # Wasm against native: verdicts, time, memory.
    out.append("\n## Wasm against the native release, per platform\n")
    out.append("| platform | both ran | same verdict | native decided, wasm not | wasm decided, native not | check time wasm/native: median | p90 | (native ≥ 50 ms: n, median, p90) | wasm first read ms (median) | wasm peak linear memory MiB: median, p90, max | Z3 max-memory MB wasm/native median |")
    out.append("|---|---|---|---|---|---|---|---|---|---|---|")
    for name in names:
        p = platforms[name]
        both = [i for i in p["wasm"] if i in p["native"]]
        same_verdict = sum(1 for i in both if p["wasm"][i]["verdict"] == p["native"][i]["verdict"])
        native_only = sum(1 for i in both if p["native"][i]["verdict"] in ("sat", "unsat") and p["wasm"][i]["verdict"] not in ("sat", "unsat"))
        wasm_only = sum(1 for i in both if p["wasm"][i]["verdict"] in ("sat", "unsat") and p["native"][i]["verdict"] not in ("sat", "unsat"))
        ratios = []
        long_ratios = []
        for i in both:
            w, n = p["wasm"][i], p["native"][i]
            if w["verdict"] in ("sat", "unsat") and n["verdict"] in ("sat", "unsat") and n.get("check_ms", 0) > 0:
                ratio = max(1, w.get("check_ms", 0)) / max(1, n["check_ms"])
                ratios.append(ratio)
                if n["check_ms"] >= 50:
                    long_ratios.append(ratio)
        startup = [r.get("startup_ms", 0) for r in p["wasm"].values()]
        peaks = [r.get("peak_memory", 0) / 1048576 for r in p["wasm"].values() if r.get("peak_memory")]
        mem = [(w.get("z3_max_memory_mb"), p["native"][i].get("z3_max_memory_mb")) for i, w in p["wasm"].items() if i in p["native"]]
        mem_ratio = [a / b for a, b in mem if a and b]
        out.append("| %s | %d | %d | %d | %d | %s | %s | %d, %s, %s | %s | %s, %s, %s | %s |" % (
            name, len(both), same_verdict, native_only, wasm_only, fmt(pct(ratios, 0.5)), fmt(pct(ratios, 0.9)),
            len(long_ratios), fmt(pct(long_ratios, 0.5)), fmt(pct(long_ratios, 0.9)), fmt(pct(startup, 0.5)),
            fmt(pct(peaks, 0.5), 1), fmt(pct(peaks, 0.9), 1), fmt(max(peaks) if peaks else None, 1), fmt(pct(mem_ratio, 0.5))))

    # Native across platforms (official release builds): do their counters agree?
    out.append("\n## The native release across platforms (for contrast)\n")
    nids = sorted(set.intersection(*[set(platforms[n]["native"]) for n in names])) if names else []
    nverdict = sum(1 for i in nids if len({platforms[n]["native"][i]["verdict"] for n in names}) == 1)
    nrlimit = sum(1 for i in nids if len({platforms[n]["native"][i].get("rlimit_count") for n in names}) == 1)
    nmodel = sum(1 for i in nids if len({platforms[n]["native"][i].get("stdout_sha256") for n in names}) == 1)
    out.append("%d problems on every platform: same verdict %d, same rlimit count %d, same transcript bytes %d (the transcript carries :time)." % (len(nids), nverdict, nrlimit, nmodel))
    wn_rlimit = sum(1 for i in nids if platforms[names[0]]["wasm"].get(i, {}).get("rlimit_count") == platforms[names[0]]["native"][i].get("rlimit_count"))
    out.append("\nwasm and native rlimit count equal on %s for %d of %d problems." % (names[0] if names else "-", wn_rlimit, len(nids)))

    # Fuel calibration.
    out.append("\n## Fuel and the resource limit\n")
    out.append("| platform | fuel per rlimit unit: p10, median, p90 | fuel per second of check (median) | rlimit per second, native (median, checks ≥ 50 ms) | start-up fuel |")
    out.append("|---|---|---|---|---|")
    for name in names:
        p = platforms[name]
        per_unit = [r["fuel_check"] / r["rlimit_count"] for r in p["wasm"].values() if r.get("rlimit_count") and r.get("fuel_check")]
        per_second = [r["fuel_check"] / (r["check_ms"] / 1000) for r in p["wasm"].values() if r.get("check_ms", 0) >= 50 and r.get("fuel_check")]
        nrate = [r["rlimit_count"] / (r["check_ms"] / 1000) for r in p["native"].values() if r.get("check_ms", 0) >= 50 and r.get("rlimit_count")]
        startup = sorted({r.get("fuel_startup") for r in p["wasm"].values()})
        out.append("| %s | %s, %s, %s | %s | %s | %s |" % (name, sci(pct(per_unit, 0.1)), sci(pct(per_unit, 0.5)), sci(pct(per_unit, 0.9)),
                                                          sci(pct(per_second, 0.5)), sci(pct(nrate, 0.5)), startup[:2]))

    # Limits.
    out.append("\n## Limit cases\n")
    out.append("| case | engine | " + " | ".join(names) + " |")
    out.append("|---|---|" + "---|" * len(names))
    cases = []
    for name in names:
        for r in platforms[name]["limits"]:
            key = (r["case"], r["engine"])
            if key not in cases:
                cases.append(key)
    for engine in ("wasm", "native"):
        cells = []
        for name in names:
            starts = [x for x in platforms[name]["limits"] if x["case"].startswith("startup_") and x["engine"] == engine]
            if not starts:
                cells.append("-")
                continue
            cells.append("%d sessions: wall median %s ms (instance made at %s ms, first read at %s ms), fuel %s" % (
                len(starts), fmt(pct([x["wall_ms"] for x in starts], 0.5)), fmt(pct([x.get("instantiate_ms", 0) for x in starts], 0.5)),
                fmt(pct([x.get("startup_ms", 0) for x in starts], 0.5)), sci(starts[0].get("fuel")) if engine == "wasm" else "-"))
        out.append("| empty session (start-up) | %s | %s |" % (engine, " | ".join(cells)))
    cases = [c for c in cases if not c[0].startswith("startup_")]
    for case, engine in cases:
        cells = []
        for name in names:
            r = next((x for x in platforms[name]["limits"] if x["case"] == case and x["engine"] == engine), None)
            if r is None:
                cells.append("-")
                continue
            text = "%s, %s exit %s" % (r.get("verdict"), r.get("end"), r.get("exit_code"))
            if r.get("reason"):
                text += ", " + r["reason"]
            if r.get("query") == "enumerate":
                text += ", %s models%s" % (r.get("count"), " complete" if r.get("complete") else "")
            if engine == "wasm":
                text += ", fuel %s" % sci(r.get("fuel"))
            text += ", peak %s MiB, %s ms" % (fmt((r.get("peak_memory") or 0) / 1048576, 0), r.get("wall_ms"))
            if r.get("errors"):
                text += ", " + " ".join(r["errors"])[:90]
            if r.get("stderr_tail"):
                text += ", stderr " + r["stderr_tail"].strip()[:60]
            cells.append(text.replace("|", "/"))
        out.append("| %s | %s | %s |" % (case, engine, " | ".join(cells)))
    limit_ids = [(c, e) for c, e in cases if e == "wasm"]
    limit_same = 0
    for case, _ in limit_ids:
        values = {json.dumps([(r.get("verdict"), r.get("fuel"), r.get("end"), r.get("count")) for r in platforms[n]["limits"] if r["case"] == case and r["engine"] == "wasm"]) for n in names}
        limit_same += len(values) == 1
    out.append("\nWasm limit cases with the same verdict, fuel, end and count on every platform: %d of %d." % (limit_same, len(limit_ids)))

    # The evaluator.
    out.append("\n## Host-side model evaluator (sat answers of the wasm runs)\n")
    if names:
        ev = platforms[names[0]]["eval"]
        out.append("| logic | sat models | validated | partial | failed | validated share |")
        out.append("|---|---|---|---|---|---|")
        logics = sorted({r["logic"] for r in ev})
        all_counts = [0, 0, 0, 0]
        for logic in logics:
            rs = [r for r in ev if r["logic"] == logic]
            c = [len(rs), sum(r["class"] == "validated" for r in rs), sum(r["class"] == "partial" for r in rs), sum(r["class"] == "failed" for r in rs)]
            all_counts = [a + b for a, b in zip(all_counts, c)]
            out.append("| %s | %d | %d | %d | %d | %s |" % (logic, c[0], c[1], c[2], c[3], fmt(100.0 * c[1] / c[0], 1) + " %" if c[0] else "-"))
        out.append("| **all** | %d | %d | %d | %d | %s |" % (all_counts[0], all_counts[1], all_counts[2], all_counts[3],
                                                            fmt(100.0 * all_counts[1] / all_counts[0], 1) + " %" if all_counts[0] else "-"))
        reasons = {}
        for r in ev:
            if r["class"] != "validated":
                reasons[r.get("reason")] = reasons.get(r.get("reason"), 0) + 1
        if reasons:
            out.append("\nNot validated, by reason: " + ", ".join("%s: %d" % kv for kv in sorted(reasons.items(), key=lambda kv: -kv[1])))
        mutants = [m for r in ev for m in r.get("mutants", [])]
        out.append("\nWrong models (one constant changed in a validated model): %d; failed %d, still validated %d, partial %d." % (
            len(mutants), sum(m["class"] == "failed" for m in mutants), sum(m["class"] == "validated" for m in mutants), sum(m["class"] == "partial" for m in mutants)))
        times = [r["us"] for r in ev if r.get("us") is not None]
        out.append("\nEvaluation time per model: median %s µs, p90 %s µs, max %s µs." % (fmt(pct(times, 0.5)), fmt(pct(times, 0.9)), fmt(max(times) if times else None)))
        same_eval = 0
        for r in ev:
            classes = {next((x["class"] for x in platforms[n]["eval"] if x["id"] == r["id"]), None) for n in names}
            same_eval += len(classes) == 1
        out.append("\nSame class on every platform: %d of %d." % (same_eval, len(ev)))

    print("\n".join(out))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
