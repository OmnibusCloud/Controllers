"""PF-1 spike: the runs' records as markdown tables.

    summary.py <folder with the downloaded artifacts> > summary.md

Reads whatever of results-kit-*, results-replay-*, results-split-*, profile-*, results-old-*,
results-rebuild, results-profile-compare and results-crafted-* is there.
"""
from __future__ import annotations

import glob
import json
import os
import sys

GIB = 1 << 30
MIB = 1 << 20


def load(path: str):
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, ValueError):
        return None


def gib(n) -> str:
    return "-" if not n else f"{n / GIB:.2f}"


def num(x) -> str:
    return f"{x:,}" if isinstance(x, int) else "-"


def mins(s) -> str:
    return "-" if s is None else f"{s / 60:.1f}"


def kits(root: str) -> None:
    print("## Kits on the submitter's side\n")
    print("| Kit | Build min | Build peak GiB | Export | Bytes | Lines | zstd -3 | zstd -19 | Export s | Export peak GiB | Format | Lean |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for kit in ("R1", "R2"):
        folder = os.path.join(root, f"results-kit-{kit}")
        build = load(os.path.join(folder, "build.json")) or {}
        export = load(os.path.join(folder, "export.json")) or {}
        b = build.get("build", {})
        for label in ("solution", "main", "challenge"):
            e = export.get(label) or {}
            meta = e.get("meta") or {}
            print(f"| {kit} | {mins(b.get('wall_s'))} | {gib(b.get('peak_rss'))} | {label} | {num(e.get('bytes'))} | "
                  f"{num(e.get('lines'))} | {num((e.get('zstd3') or {}).get('bytes'))} | {num((e.get('zstd19') or {}).get('bytes'))} | "
                  f"{e.get('wall_s', '-')} | {gib(e.get('peak_rss'))} | {(meta.get('format') or {}).get('version', '-')} | "
                  f"{(meta.get('lean') or {}).get('version', '-')} |")
        print()
        for name in ("driver.json", "judge.json"):
            record = load(os.path.join(folder, name))
            if not record:
                continue
            print(f"**{kit} {name[:-5]}**\n")
            print("| Case | Exit | Wall s | Peak GiB | Verdict / message |")
            print("|---|---|---|---|---|")
            for case, row in record.items():
                if not isinstance(row, dict):
                    continue
                result = row.get("result") or {}
                what = result.get("verdict") or ""
                if result:
                    what += " " + json.dumps({k: result.get(k) for k in ("constants", "library_same", "library_mismatched",
                                                                         "own", "parse_ms", "import_ms", "compare_ms",
                                                                         "statement_ms", "replay_ms", "total_ms")
                                              if k in result})
                    what += " " + str(result.get("message", ""))[:160]
                else:
                    what = (row.get("says") or "")[-200:]
                print(f"| {case} | {row.get('exit')} | {row.get('wall_s')} | {gib(row.get('peak_rss'))} | {what.strip()} |")
            print()


def replays(root: str) -> None:
    print("## Full stage per platform\n")
    print("| Kit | Platform | Step | Exit | Wall min | CPU min | Peak GiB | Killed | Says |")
    print("|---|---|---|---|---|---|---|---|---|")
    for path in sorted(glob.glob(os.path.join(root, "results-replay-*", "replay.json"))):
        record = load(path) or {}
        label = record.get("label", os.path.basename(os.path.dirname(path)))
        kit, _, rid = label.partition("-")
        for step, row in (record.get("steps") or {}).items():
            if "skipped" in row:
                print(f"| {kit} | {rid} | {step} | skipped ({row['skipped']}) | | | | | |")
                continue
            cpu = (row.get("user_s") or 0) + (row.get("sys_s") or 0)
            print(f"| {kit} | {rid} | {step} | {row.get('exit')} | {mins(row.get('wall_s'))} | {mins(cpu)} | "
                  f"{gib(row.get('peak_rss'))} | {row.get('killed') or ''} | {(row.get('says') or '')[-120:]} |")
    print()


def splits(root: str) -> None:
    for path in sorted(glob.glob(os.path.join(root, "results-split-*", "split.json"))):
        record = load(path) or {}
        steps = record.get("steps") or {}
        print(f"## Split: {record.get('label')}\n")
        prof = steps.get("driver-profile") or {}
        result = prof.get("result") or {}
        times = prof.get("times") or {}
        print(f"Profile: exit {prof.get('exit')}, wall {mins(prof.get('wall_s'))} min, peak {gib(prof.get('peak_rss'))} GiB, "
              f"verdict {result.get('verdict')}, parse {result.get('parse_ms')} ms, replay {result.get('replay_ms')} ms; "
              f"declarations {times.get('declarations')}, kernel total {times.get('total_s')} s, slowest {times.get('max_s')} s, "
              f"shares {times.get('shares')}\n")
        for entry in (times.get("slowest") or [])[:8]:
            print(f"- {entry['name']}: {entry['s']} s")
        print(f"\nBy kind: {times.get('by_kind')}\n")
        print("| Slice | Exit | Wall min | Peak GiB | Verdict | Parse ms | Load ms | Check ms | Slice constants | Unchecked |")
        print("|---|---|---|---|---|---|---|---|---|---|")
        for index, part in enumerate((steps.get("driver-slices") or {}).get("slices", [])):
            r = part.get("result") or {}
            print(f"| {index} | {part.get('exit')} | {mins(part.get('wall_s'))} | {gib(part.get('peak_rss'))} | {r.get('verdict')} | "
                  f"{r.get('parse_ms')} | {r.get('load_ms')} | {r.get('check_ms')} | {r.get('slice_constants')} | {r.get('unchecked_constants')} |")
        print()


def profiles(root: str) -> None:
    print("## Profiles\n")
    for path in sorted(glob.glob(os.path.join(root, "profile-*", "fetch.json"))) + \
            sorted(glob.glob(os.path.join(root, "results-rebuild", "fetch.json"))):
        r = load(path) or {}
        print(f"- {os.path.basename(os.path.dirname(path))}: rev {r.get('rev')}, toolchain {r.get('lean_toolchain')}, "
              f"clone {r.get('clone_s')} s, tool {r.get('tool', {}).get('wall_s')} s, download {r.get('download', {}).get('wall_s')} s, "
              f"unpack {r.get('unpack', {}).get('wall_s')} s; ltar {r.get('ltar')}; files {(r.get('files') or {}).get('files')} "
              f"({(r.get('files') or {}).get('bytes')} bytes, sha {(r.get('files') or {}).get('sha256', '')[:12]}); by kind "
              f"{ {k: v['sha256'][:12] for k, v in ((r.get('files') or {}).get('by_kind') or {}).items()} }; probe exit {r.get('probe', {}).get('exit')}")
    for path in sorted(glob.glob(os.path.join(root, "profile-https-*", "https.json"))):
        r = load(path) or {}
        print(f"- {os.path.basename(os.path.dirname(path))}: {json.dumps({k: v for k, v in r.items() if k not in ('files',)})}; "
              f"files {json.dumps({k: v for k, v in (r.get('files') or {}).items() if k != 'by_kind'})}")
    compare = load(os.path.join(root, "results-profile-compare", "compare.json"))
    if compare:
        print("\n| Pair | Left | Right | Same | Different | Only left | Only right |")
        print("|---|---|---|---|---|---|---|")
        for pair, c in compare.items():
            print(f"| {pair} | {c['left']} | {c['right']} | {c['same']} | {c['different']} | {c['only_left']} | {c['only_right']} |")
    print()
    for path in sorted(glob.glob(os.path.join(root, "results-old-*", "old-*.json"))):
        r = load(path) or {}
        print(f"### Old revision {r.get('name')} ({r.get('commit', '')[:8]}, Lean {r.get('lean')})\n")
        for key in ("tool", "tool_today_endpoint"):
            if key in r:
                t = r[key]
                print(f"- {key}: exit {t.get('exit')}, {t.get('wall_s')} s, ltar {t.get('ltar_files')} ({t.get('ltar_bytes')} B), "
                      f"oleans {t.get('oleans')} of {r.get('mathlib_modules')} modules, urls {t.get('urls')}; tail: {' | '.join(t.get('tail', [])[-4:])}")
        print(f"- served fraction: {r.get('served_fraction')}; export with Mathlib: {r.get('export_with_mathlib')}")
        if "lean4export" in r:
            print(f"- lean4export {r['lean4export'].get('tag')}: build exit {r['lean4export'].get('build', {}).get('exit')}")
        for module in ("PF1Old", "PF1OldChallenge"):
            e = r.get(f"export_{module}")
            if e:
                print(f"- export {module}: exit {e.get('exit')}, {e.get('bytes')} bytes, meta {json.dumps(e.get('meta'))}")
        for kernel, row in (r.get("pinned_readers") or {}).items():
            print(f"- pinned {kernel}: exit {row.get('exit')}, {row.get('wall_s')} s, {(row.get('says') or '')[-160:]}")
        print()
    rebuild = load(os.path.join(root, "results-rebuild", "rebuild.json"))
    if rebuild:
        print("### Rebuild from source\n")
        print(json.dumps({k: v for k, v in rebuild.items()}, indent=1)[:4000])
        print()


def crafted(root: str) -> None:
    for path in sorted(glob.glob(os.path.join(root, "results-crafted-*", "crafted.json"))):
        record = load(path) or {}
        rid = os.path.basename(os.path.dirname(path))[len("results-crafted-"):]
        programs = record.get("programs", [])
        print(f"## Crafted exports on {rid} (cap {record.get('cap_s')} s, {record.get('memory_gib')} GiB)\n")
        print("| Input | " + " | ".join(programs) + " |")
        print("|---|" + "---|" * len(programs))
        for label, entry in (record.get("results") or {}).items():
            cells = []
            for program in programs:
                row = (entry.get("runs") or {}).get(program)
                if not row:
                    cells.append("")
                    continue
                cells.append(f"{row.get('ended')} ({row.get('wall_s', 0):.1f} s, {row.get('peak_rss', 0) / MIB:.0f} MiB)")
            print(f"| {label} | " + " | ".join(cells) + " |")
        print()


def main() -> int:
    root = sys.argv[1]
    kits(root)
    replays(root)
    splits(root)
    profiles(root)
    crafted(root)
    return 0


if __name__ == "__main__":
    sys.exit(main())
