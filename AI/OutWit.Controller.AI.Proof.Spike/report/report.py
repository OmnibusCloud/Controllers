"""PF-0 spike: every platform's records side by side.

    report.py <folder with results-<rid>/ and confirm-<from>-on-<rid>/ folders>

Prints a Markdown report: toolchain and environment per platform (and whether the environment
is the same bytes everywhere), start-up, the golden set's verdicts and heartbeats compared
across the platforms, the pool path against the fresh one, the slice replay, the two-stage
split with its confirmations on other machines, memory of several processes, the sandbox.
Exits 1 when a platform accepted an item that another rejected inside the budget.
"""
from __future__ import annotations

import json
import os
import statistics
import sys

ORDER = ["win-x64", "linux-x64", "osx-arm64", "linux-arm64", "osx-x64", "win-arm64"]
V1 = ["win-x64", "linux-x64", "osx-arm64"]
# Outcomes that the machine decides, not the proof: never compared.
MACHINE = {"timeout", "memout", "engine_crash", "no_report", "none"}
DECIDED_AGAINST = {"rejected", "incomplete", "axioms_refused", "statement_mismatch"}
ENVIRONMENT_KINDS = [".olean", ".olean.server", ".olean.private", ".ir"]


def load(path: str):
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, ValueError):
        return None


def load_rows(path: str) -> dict:
    rows = {}
    try:
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if line:
                    row = json.loads(line)
                    rows[row["id"]] = row
    except (OSError, ValueError):
        pass
    return rows


def mib(value) -> str:
    return "" if not value else f"{value / (1 << 20):,.0f}"


def gib(value) -> str:
    return "" if not value else f"{value / (1 << 30):.2f}"


def outcome(row: dict) -> str:
    return row.get("verdict") or row.get("status") or "none"


def median(values: list):
    values = [value for value in values if value is not None]
    if not values:
        return None
    value = statistics.median(values)
    return round(value, 2) if isinstance(value, float) else value


def table(header: list, rows: list) -> list:
    lines = ["| " + " | ".join(header) + " |", "|" + "---|" * len(header)]
    for row in rows:
        lines.append("| " + " | ".join("" if cell is None else str(cell) for cell in row) + " |")
    return lines + [""]


def main() -> int:
    root = sys.argv[1]
    rids = [rid for rid in ORDER if os.path.isdir(os.path.join(root, f"results-{rid}"))]

    def path(rid: str, name: str) -> str:
        return os.path.join(root, f"results-{rid}", name)

    out = ["## PF-0: the platforms side by side", ""]
    hard = 0

    # ------------------------------------------------------------------ machines
    rows = []
    for rid in rids:
        machine = load(path(rid, "machine.json")) or {}
        rows.append([rid, machine.get("cpu_model"), machine.get("cpus"), gib(machine.get("memory_bytes")),
                     f"{machine.get('system')} {machine.get('release')}", machine.get("image_version")])
    out += ["### Runners", ""] + table(["platform", "CPU", "cores", "RAM GiB", "system", "image"], rows)

    # ------------------------------------------------------------------ toolchain and environment
    rows = []
    for rid in rids:
        tool = load(path(rid, "toolchain.json")) or {}
        rows.append([rid, f"{tool.get('archive_bytes', 0):,}", (tool.get("sha256") or "")[:16] + "...",
                     "yes" if tool.get("sha256") and tool.get("sha256") == tool.get("pinned_sha256") else "NO",
                     tool.get("download_s"), tool.get("unpack_s"), gib(tool.get("unpacked_bytes")),
                     tool.get("unpacked_files")])
    out += ["### Lean toolchain (official release, pinned)", ""] + table(
        ["platform", "archive bytes", "SHA-256", "as pinned", "download s", "unpack s", "on disk GiB", "files"], rows)

    rows = []
    digests = {}
    for rid in rids:
        env = load(path(rid, "mathlib.json")) or {}
        kinds = env.get("disk_by_kind") or {}
        needed = sum((kinds.get(kind) or {}).get("bytes", 0) for kind in ENVIRONMENT_KINDS)
        digests[rid] = tuple((env.get("manifest_sha256_by_kind") or {}).get(kind, "?")[:12] for kind in ENVIRONMENT_KINDS)
        step = lambda name: (env.get(name) or {}).get("wall_s")  # noqa: E731
        rows.append([rid, f"{env.get('download_bytes', 0):,}", env.get("download_files"), step("clone"),
                     step("resolve_and_build_tool"), step("download"), step("unpack"), env.get("hash_s"),
                     gib(env.get("disk_bytes")), gib(needed), " ".join(digests[rid])])
    out += ["### Mathlib environment by Mathlib's cache tool", ""] + table(
        ["platform", "bytes downloaded", "files", "clone s", "deps + tool s", "download s", "unpack s",
         "hash all s", "on disk GiB", "of it .olean* + .ir GiB", "digests (.olean, .server, .private, .ir)"], rows)
    if digests:
        same = len(set(digests.values())) == 1
        out += [f"The compiled files Lean reads are **{'the same bytes' if same else 'NOT the same bytes'}** "
                f"on {len(digests)} platforms (SHA-256 over path, size and digest of every file, by kind).", ""]
    for rid in rids:
        env = load(path(rid, "mathlib.json")) or {}
        if env.get("disk_by_kind"):
            out += [f"On disk by kind ({rid}): " + ", ".join(
                f"`{kind}` {value['bytes'] / (1 << 20):,.0f} MiB in {value['files']} files"
                for kind, value in sorted(env["disk_by_kind"].items(), key=lambda kv: -kv[1]["bytes"])), ""]
            break
    pack = None
    for rid in rids:
        pack = pack or load(path(rid, "pack.json"))
    if pack:
        rows = [[entry["name"], f"{entry['bytes']:,}", entry.get("seconds"), entry.get("parts_2gib"),
                 entry.get("unpack_s")] for entry in pack.get("variants", [])]
        out += ["Packing the environment (one runner):", ""] + table(
            ["packing", "bytes", "seconds to pack", "parts of at most 2 GiB", "seconds to unpack"], rows)
        first = pack["variants"][0] if pack.get("variants") else {}
        if first:
            out += [f"The `.ltar` asset unpacked by the toolchain's `leantar` alone gives "
                    f"{'the same files as' if first.get('same_files_as_the_cache_tool') else 'files that DIFFER from'} "
                    f"Mathlib's cache tool; `lean` "
                    f"{'imports Mathlib from it' if first.get('lean_imports_from_it') else 'does NOT import from it'}.", ""]
        if pack.get("closures"):
            rows = [[entry["root"], entry["modules"], gib(entry["bytes"]), gib(entry.get("needed_bytes")),
                     mib(entry.get("download_bytes")), entry.get("download_files")]
                    for entry in pack["closures"]]
            out += ["Smaller environments (the import closure of one root):", ""] + table(
                ["root", "modules", "all files GiB", ".olean* + .ir GiB", "cache download MiB", "cache files"], rows)

    # ------------------------------------------------------------------ start-up
    rows = []
    for rid in rids:
        start = load(path(rid, "startup.json")) or {}
        runs = start.get("mathlib") or []
        good = [run for run in runs if run.get("exit") == 0]
        core = [run for run in (start.get("core") or []) if run.get("exit") == 0]
        small = [run for run in (start.get("small") or []) if run.get("exit") == 0]
        if not runs:
            continue
        warm = good[1:] or good
        rows.append([rid, runs[0].get("wall_s") if runs[0].get("exit") == 0 else f"failed ({runs[0].get('killed')})",
                     median([run["wall_s"] for run in warm]), median([run["user_s"] for run in warm]),
                     median([run["sys_s"] for run in warm]), mib(median([run["peak_rss"] for run in good])),
                     mib(median([run.get("peak_private") for run in good])),
                     median([run["wall_s"] for run in small[1:] or small]),
                     mib(median([run["peak_rss"] for run in small])),
                     median([run["wall_s"] for run in core[1:] or core])])
    out += ["### Start-up: a process that only imports the environment", ""] + table(
        ["platform", "first run s", "next runs s", "user s", "system s", "peak resident MiB", "peak private MiB",
         "the smaller profile s", "its peak resident MiB", "core Lean only s"], rows)

    # ------------------------------------------------------------------ golden set
    golden = {rid: load_rows(path(rid, "golden-fresh.jsonl")) for rid in rids}
    golden = {rid: rows for rid, rows in golden.items() if rows}
    pooled_golden = {rid: load_rows(path(rid, "golden-pool.jsonl")) for rid in rids}
    pooled_golden = {rid: rows for rid, rows in pooled_golden.items() if rows}
    small_golden = {rid: load_rows(path(rid, "golden-small.jsonl")) for rid in rids}
    small_golden = {rid: rows for rid, rows in small_golden.items() if rows}
    one_golden = {rid: load_rows(path(rid, "golden-pool-one.jsonl")) for rid in rids}
    one_golden = {rid: rows for rid, rows in one_golden.items() if rows}
    for data, name, heading in ((golden, "golden-fresh", "Golden set, one process per item"),
                                (pooled_golden, "golden-pool", "Golden set, pooled processes"),
                                (one_golden, "golden-pool-one",
                                 "Golden set, one pooled process taking the items in one fixed order"),
                                (small_golden, "golden-small",
                                 "Golden set against a smaller environment, one process per item")):
        if not data:
            continue
        verdicts = sorted({outcome(row) for rows in data.values() for row in rows.values()})
        rows = []
        for rid, items in data.items():
            summary = load(path(rid, name + "-summary.json")) or {}
            counts = {verdict: sum(1 for row in items.values() if outcome(row) == verdict) for verdict in verdicts}
            rows.append([rid, len(items)] + [counts[verdict] or "" for verdict in verdicts]
                        + [summary.get("jobs") or summary.get("workers"), summary.get("elapsed_s"),
                           summary.get("items_per_minute")])
        out += [f"### {heading}", ""] + table(
            ["platform", "items"] + verdicts + ["at a time", "seconds", "items per minute"], rows)

        def compare(platforms: list, title: str, data: dict = data) -> int:
            nonlocal out
            golden = data
            platforms = [rid for rid in platforms if rid in golden]
            if len(platforms) < 2:
                return 0
            ids = sorted(set.intersection(*[set(golden[rid]) for rid in platforms]))
            same_verdict = same_heartbeats = compared_heartbeats = same_axioms = same_term = compared_term = 0
            hard_rows, flips, others, beats = [], [], [], []
            for item in ids:
                each = {rid: golden[rid][item] for rid in platforms}
                seen = {rid: outcome(row) for rid, row in each.items()}
                values = set(seen.values())
                if len(values) == 1:
                    same_verdict += 1
                elif "accepted" in values and values & DECIDED_AGAINST:
                    hard_rows.append([item] + [seen[rid] for rid in platforms])
                elif values & (MACHINE | {"budget"}):
                    flips.append([item] + [seen[rid] for rid in platforms])
                else:
                    others.append([item] + [seen[rid] for rid in platforms])
                if not values & MACHINE:
                    counts = [each[rid].get("heartbeats") for rid in platforms]
                    if all(count is not None for count in counts):
                        compared_heartbeats += 1
                        if len(set(counts)) == 1:
                            same_heartbeats += 1
                        else:
                            spread = (max(counts) - min(counts)) / max(1, max(counts))
                            beats.append((spread, [item] + counts + [f"{100 * spread:.3f} %"]))
                    if len({json.dumps(each[rid].get("axioms")) for rid in platforms}) == 1:
                        same_axioms += 1
                    terms = [each[rid].get("value_hash") for rid in platforms]
                    if values == {"accepted"} and all(terms):
                        compared_term += 1
                        same_term += 1 if len(set(terms)) == 1 else 0
            out += [f"**{title}** ({', '.join(platforms)}): {len(ids)} items on all of them; "
                    f"the same verdict on **{same_verdict}**; accepted on one and decided against on another: "
                    f"**{len(hard_rows)}**; differences at the budget or by the machine (flips): {len(flips)}; "
                    f"other differences: {len(others)}. Heartbeats: the same count on **{same_heartbeats} of "
                    f"{compared_heartbeats}**; the same axioms on {same_axioms}; the same proof term (hash) on "
                    f"{same_term} of {compared_term} accepted.", ""]
            for name, found in (("Accepted against decided", hard_rows), ("Flips", flips), ("Other differences", others)):
                if found:
                    out += [f"{name}:", ""] + table(["item"] + platforms, found[:40])
            if beats:
                beats.sort(key=lambda entry: -entry[0])
                out += [f"Heartbeat counts that differ ({len(beats)}; the widest first):", ""] + table(
                    ["item"] + platforms + ["spread"], [row for _spread, row in beats[:25]])
            return len(hard_rows)

        hard += compare(V1, "The three platforms the client ships")
        compare(ORDER, "All six")

        # Platform by platform against the one that checked the most items: a platform that ran
        # under a time budget has checked a prefix of the same mixed order.
        reference = max(data, key=lambda rid: (len(data[rid]), -ORDER.index(rid)))
        rows = []
        for rid, items in data.items():
            if rid == reference:
                continue
            common = [item for item in items if item in data[reference]]
            seen = [(outcome(items[item]), outcome(data[reference][item])) for item in common]
            same = sum(1 for a, b in seen if a == b)
            against = sum(1 for a, b in seen if a != b and "accepted" in (a, b) and {a, b} & DECIDED_AGAINST)
            pairs = [(items[item].get("heartbeats"), data[reference][item].get("heartbeats")) for item in common
                     if not {outcome(items[item]), outcome(data[reference][item])} & MACHINE]
            pairs = [(a, b) for a, b in pairs if a is not None and b is not None]
            equal = sum(1 for a, b in pairs if a == b)
            widest = max([abs(a - b) for a, b in pairs] or [0])
            accepted = [item for item in common if outcome(items[item]) == outcome(data[reference][item]) == "accepted"]
            terms = sum(1 for item in accepted if items[item].get("value_hash") == data[reference][item].get("value_hash"))
            errors = sum(1 for item in common if items[item].get("error_digest") == data[reference][item].get("error_digest"))
            hard += against
            rows.append([rid, len(common), same, against, len(common) - same - against, f"{equal} of {len(pairs)}",
                         widest, f"{terms} of {len(accepted)}", f"{errors} of {len(common)}"])
        if rows:
            out += [f"Each platform against {reference} (the items both checked):", ""] + table(
                ["platform", "items in common", "same verdict", "accepted against decided", "other differences",
                 "same heartbeat count", "widest difference, heartbeats", "same proof term (hash)",
                 "same error messages (digest)"], rows)

        # the list's own expectation (what the selecting runner said, by the fresh-process path)
        rows = []
        for rid, items in data.items():
            expected = [row for row in items.values() if row.get("expect")]
            if expected:
                agree = sum(1 for row in expected if row["expect"] == outcome(row))
                rows.append([rid, len(expected), agree])
        if rows:
            out += table(["platform", "items with a listed verdict", "same as listed"], rows)

    # ------------------------------------------------------------------ the smaller environment against the whole
    rows = []
    for rid, small in small_golden.items():
        full = golden.get(rid) or {}
        both = [item for item in small if item in full]
        accepted = [item for item in both if outcome(full[item]) == "accepted"]
        kept = [item for item in accepted if outcome(small[item]) == "accepted"]
        same_count = sum(1 for item in kept if small[item].get("heartbeats") == full[item].get("heartbeats"))
        summary = load(path(rid, "golden-small-summary.json")) or {}
        wall_small = median([small[item]["wall_s"] for item in both])
        wall_full = median([full[item]["wall_s"] for item in both])
        rows.append([rid, " ".join(summary.get("imports") or []), len(both), len(accepted), len(kept),
                     same_count, wall_small, wall_full,
                     mib(median([small[item].get("peak_rss") for item in both])),
                     mib(median([full[item].get("peak_rss") for item in both]))])
    if rows:
        out += ["### The smaller environment against the whole of Mathlib (the same items, the same runner)", ""] + table(
            ["platform", "imports", "items in both runs", "accepted with all of Mathlib", "of them accepted with the smaller one",
             "of them with the same heartbeats", "process s (median), smaller", "process s, whole",
             "peak resident MiB, smaller", "peak resident MiB, whole"], rows)

    # ------------------------------------------------------------------ options that touch the count
    rows = []
    for rid in rids:
        for name, label, main_rows in (
                ("golden-again", "fresh: the same options once more", golden.get(rid) or {}),
                ("golden-async", "fresh: parallel elaboration on (`-DElab.async=true`)", golden.get(rid) or {}),
                ("golden-threads1", "fresh: one worker thread (`--threads=1`)", golden.get(rid) or {}),
                ("golden-pool-one", "pool: one process in the list's order, against the pool run above",
                 pooled_golden.get(rid) or {})):
            other = load_rows(path(rid, name + ".jsonl"))
            if not other or not main_rows:
                continue
            both = [item for item in other if item in main_rows and outcome(other[item]) not in MACHINE
                    and outcome(main_rows[item]) not in MACHINE]
            same = sum(1 for item in both if outcome(other[item]) == outcome(main_rows[item]))
            pairs = [(other[item].get("heartbeats"), main_rows[item].get("heartbeats")) for item in both]
            pairs = [(a, b) for a, b in pairs if a and b]
            equal = sum(1 for a, b in pairs if a == b)
            ratio = median([a / b for a, b in pairs])
            wall = sum(other[item]["wall_s"] for item in both)
            cpu = sum(other[item].get("user_s", 0) + other[item].get("sys_s", 0) for item in both)
            base_wall = sum(main_rows[item]["wall_s"] for item in both)
            base_cpu = sum(main_rows[item].get("user_s", 0) + main_rows[item].get("sys_s", 0) for item in both)
            rows.append([rid, label, len(both), same, f"{equal} of {len(pairs)}", ratio,
                         round(wall, 1), round(cpu / wall, 2) if wall and cpu else None,
                         round(base_wall, 1), round(base_cpu / base_wall, 2) if base_wall and base_cpu else None])
    if rows:
        out += ["### What the heartbeat count depends on (the same items, one runner)", ""] + table(
            ["platform", "variant", "items", "same verdict", "same heartbeats", "heartbeats / main run (median)",
             "wall s", "CPU / wall", "main run wall s", "main run CPU / wall"], rows)

    # ------------------------------------------------------------------ pool against fresh
    rows = []
    for rid in rids:
        fresh = golden.get(rid) or load_rows(path(rid, "smoke-mathlib.jsonl"))
        name = "golden-pool" if golden.get(rid) else "smoke-mathlib-pool"
        pooled = load_rows(path(rid, name + ".jsonl"))
        summary = load(path(rid, name + "-summary.json")) or {}
        fresh_summary = load(path(rid, "golden-fresh-summary.json")) or {}
        if not pooled:
            continue
        both = [item for item in pooled if item in fresh]
        same = sum(1 for item in both if outcome(pooled[item]) == outcome(fresh[item]))
        pairs = [(pooled[item].get("heartbeats"), fresh[item].get("heartbeats")) for item in both]
        pairs = [(a, b) for a, b in pairs if a and b]
        equal = sum(1 for a, b in pairs if a == b)
        ratio = median([a / b for a, b in pairs])
        processes = summary.get("processes") or []
        imports = median([process.get("import_s") for process in processes])
        after = median([(process.get("after_import") or {}).get("private") for process in processes])
        end = median([(process.get("at_end") or {}).get("private") for process in processes])
        rows.append([rid, len(both), same, f"{equal} of {len(pairs)}", f"{ratio:.3f}" if ratio else "",
                     summary.get("workers"), summary.get("items_per_minute"),
                     summary.get("items_per_minute_after_import"), fresh_summary.get("items_per_minute"),
                     imports, mib(after), mib(end)])
    if rows:
        out += ["### Pooled processes against one process per item (the same items)", ""] + table(
            ["platform", "items", "same verdict", "same heartbeats", "heartbeats pool / fresh (median)",
             "processes", "pool items per minute", "after the import", "fresh items per minute",
             "import s", "private MiB after import", "private MiB at the end"], rows)

    # ------------------------------------------------------------------ replay
    rows = []
    for rid in rids:
        replay = load(path(rid, "replay.json"))
        if not replay:
            continue
        together = replay.get("together") or []
        modules = replay.get("modules") or []
        if isinstance(together, dict):
            together = [dict(together, modules=len(modules))]
        literal = [module for module in modules if module.get("kind") == "bignum"]
        rows.append([rid, len(modules), len(literal), replay.get("replayed"), replay.get("failed"), replay.get("wall_s"),
                     replay.get("cpu_s"), median([module["wall_s"] for module in modules]),
                     max([module["wall_s"] for module in modules] or [0]), mib(replay.get("peak_rss_max")),
                     mib(max([module.get("peak_private") or 0 for module in modules] or [0])),
                     "; ".join(f"{entry['modules']}: exit {entry['exit']}, {entry['wall_s']:.0f} s, "
                               f"{mib(entry['peak_rss'])} MiB" for entry in together)])
    if rows:
        out += ["### Replay of a slice of the environment (`leanchecker`, the files built elsewhere)", ""] + table(
            ["platform", "modules", "of them with literals beyond a machine word", "replayed", "failed",
             "one by one s", "CPU s", "median s", "longest s", "largest peak resident MiB",
             "largest peak private MiB", "several modules in one process"], rows)

    # ------------------------------------------------------------------ split
    rows = []
    for rid in rids:
        split = load(path(rid, "split-export.json"))
        if not split:
            continue
        done = [row for row in split if row.get("comparator")]
        accepted = [row for row in done if row["comparator"]["exit"] == 0]
        refused = [row for row in done if row["comparator"]["exit"] == 1]
        pick = lambda key, sub: median([row[key].get(sub) for row in accepted if row.get(key)])  # noqa: E731
        rows.append([rid, len(split), len(accepted), len(refused), len(split) - len(done),
                     pick("solution", "elab_s"), pick("solution", "export_s"),
                     mib(pick("solution", "export_bytes")), mib(max([row["solution"].get("export_bytes", 0) for row in done] or [0])),
                     mib(pick("transfer", "zst_bytes")), pick("comparator", "wall_s"), pick("kernel", "wall_s"),
                     mib(pick("comparator", "peak_rss")), pick("paranoid", "wall_s")])
    if rows:
        out += ["### Two-stage split, both stages on one machine", ""] + table(
            ["platform", "items", "judged accepted", "judged refused", "no export", "elaborate s (median)",
             "export s", "export MiB", "largest export MiB", "compressed MiB", "`lake comparator` s",
             "kernel alone s", "comparator peak resident MiB", "with every bundled checker s"], rows)

    rows = []
    for name in sorted(os.listdir(root)):
        if not name.startswith("confirm-"):
            continue
        for entry in sorted(os.listdir(os.path.join(root, name))):
            if not entry.startswith("split-confirm-"):
                continue
            confirm = load(os.path.join(root, name, entry)) or []
            done = [row for row in confirm if row.get("comparator")]
            agree = sum(1 for row in done if row["comparator"]["exit"] == row.get("sender_comparator_exit"))
            accepted = [row for row in done if row["comparator"]["exit"] == 0]
            sandboxed = [row for row in done if row.get("comparator_sandboxed")]
            sandbox_agree = sum(1 for row in sandboxed if row["comparator_sandboxed"]["exit"] == row["comparator"]["exit"])
            rows.append([name[len("confirm-"):], len(confirm), len(accepted),
                         sum(1 for row in done if row["comparator"]["exit"] == 1), f"{agree} of {len(done)}",
                         sum(1 for row in confirm if row.get("solution_sha256_matches")),
                         sum(1 for row in confirm if row.get("challenge_export_same_as_sender")),
                         median([row["challenge"].get("elab_s") for row in done]),
                         median([row["challenge"].get("export_s") for row in done]),
                         median([row["comparator"]["wall_s"] for row in accepted]),
                         mib(median([row["comparator"]["peak_rss"] for row in accepted])),
                         f"{sandbox_agree} of {len(sandboxed)}" if sandboxed else "",
                         median([row["comparator_sandboxed"]["wall_s"] for row in sandboxed
                                 if row["comparator_sandboxed"]["exit"] == 0])])
    if rows:
        out += ["### Confirmation on another machine (the export travelled as an artifact)", ""] + table(
            ["from, on", "exports", "confirmed", "refused", "same judgement as the sender",
             "export arrived intact", "challenge export the same bytes as the sender's", "challenge elaborate s",
             "challenge export s", "`lake comparator` s (median, confirmed)", "peak resident MiB",
             "same judgement inside bubblewrap", "inside bubblewrap s"], rows)

    # ------------------------------------------------------------------ degree
    rows = []
    for rid in rids:
        degree = load(path(rid, "degree.json"))
        if not degree or not degree.get("steps"):
            continue
        steps = degree["steps"]
        base = (degree.get("baseline") or {}).get("available")
        left = [step["system"].get("available") for step in steps]
        first = base - left[0] if base and left[0] else None
        marginal = [left[index - 1] - left[index] for index in range(1, len(left)) if left[index] and left[index - 1]]
        each = steps[-1]["each"]
        # Windows: the commit charge in use (the limit moves when the system grows its page file).
        used = [(entry.get("commit_limit") or 0) - (entry.get("commit_available") or 0)
                for entry in [degree.get("baseline") or {}] + [step["system"] for step in steps]]
        commit_step = [used[index] - used[index - 1] for index in range(1, len(used)) if used[index] and used[index - 1]]
        rows.append([rid, len(steps), mib(base), mib(first), mib(median(marginal)), mib(left[-1]),
                     mib(median([one.get("rss") for one in each])), mib(median([one.get("private") for one in each])),
                     mib(median(commit_step)), median([step["import_s"] for step in steps]), degree.get("stopped") or ""])
    if rows:
        out += ["### Memory of several pooled processes with the environment imported", ""] + table(
            ["platform", "processes started", "machine had left MiB", "the first took MiB",
             "each further one took MiB (median)", "left at the end MiB", "resident of one MiB",
             "private of one MiB", "commit charge of each one MiB (Windows)", "import s", "stopped because"], rows)

    # ------------------------------------------------------------------ sandbox
    rows = []
    for rid in rids:
        sandbox = load(path(rid, "sandbox.json"))
        if not sandbox:
            continue
        holds = sandbox.get("holds") or {}
        items = sandbox.get("items") or {}
        bwrap = sandbox.get("bwrap") or {}
        slow = None
        if items.get("plain_wall_s") and items.get("landrun_wall_s"):
            slow = f"{items['landrun_wall_s'] / items['plain_wall_s']:.3f}"
        rows.append([rid, sandbox.get("kernel"), sandbox.get("landlock_abi"),
                     "refused" if holds.get("write_outside_refused") else "NOT refused",
                     "refused" if holds.get("tcp_refused") else "NOT refused",
                     "refused" if holds.get("udp_refused") else "not refused",
                     f"{items.get('same_verdict_and_heartbeats')} of {items.get('count')}",
                     items.get("plain_wall_s"), items.get("landrun_wall_s"), slow,
                     bwrap.get("installed") or "not installed", bwrap.get("userns_restricted")])
    if rows:
        out += ["### The Linux sandbox (`landrun`), by plain checks", ""] + table(
            ["platform", "kernel", "Landlock ABI", "write outside the scratch folder", "TCP connection",
             "UDP datagram", "same verdict and heartbeats inside", "plain s", "inside s", "inside / plain",
             "bwrap on the image", "user namespaces restricted"], rows)

    print("\n".join(out))
    return 1 if hard else 0


if __name__ == "__main__":
    sys.exit(main())
