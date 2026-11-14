"""Runs the determinism plan of one cell (one binary on one runner, optionally under an
emulator prefix) and writes canonical records, measurements and facts. No binary is written
to the output folder.

usage: python cell.py --engine EXE --cell NAME --out DIR --positions positions.jsonl
                      [--sfbench src/benchmark.cpp] [--plan native|emu|lite|official]
                      [--prefix "sde|-skx|--"] [--procs N] [--build-json build.json]
                      [--low-priority]

Output: DIR/NAME/facts.json, DIR/NAME/metrics.json, DIR/NAME/records/<variant>/<config>.jsonl
"""

import argparse
import hashlib
import json
import os
import platform
import queue
import shutil
import statistics
import subprocess
import sys
import tempfile
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import records  # noqa: E402
from positions import load_golden, load_sfbench  # noqa: E402
from sfuci import Engine, EngineError, run_command  # noqa: E402

# name -> (mode, nodes per search, MultiPV). action_values: nodes per successor search.
CONFIGS = {
    "an-10k-pv1": ("analyse", 10000, 1),
    "an-10k-pv4": ("analyse", 10000, 4),
    "an-1m-pv1": ("analyse", 1000000, 1),
    "an-1m-pv4": ("analyse", 1000000, 4),
    "av-10k": ("action_values", 10000, 1),
}

ALL = None


def plan_of(name, procs):
    """(variant, config, first-K positions or ALL, processes, fresh process per search)."""
    if name == "native":
        return ([("main", c, ALL, procs, False) for c in CONFIGS]
                + [("serial", "an-10k-pv1", ALL, 1, False),
                   ("serial", "an-10k-pv4", ALL, 1, False),
                   ("serial", "av-10k", ALL, 1, False),
                   ("serial", "an-1m-pv1", 32, 1, False),
                   ("fresh", "an-10k-pv1", ALL, procs, True),
                   ("fresh", "an-10k-pv4", ALL, procs, True),
                   ("fresh", "av-10k", 40, procs, True),
                   ("fresh", "an-1m-pv1", 32, procs, True),
                   ("p16", "an-10k-pv1", ALL, 16, False),
                   ("p16", "av-10k", 100, 16, False)])
    if name == "lite":
        return [("main", c, ALL, procs, False) for c in CONFIGS]
    if name == "emu":
        return [("main", "an-10k-pv1", ALL, procs, False),
                ("main", "an-10k-pv4", ALL, procs, False),
                ("main", "av-10k", 12, procs, False),
                ("main", "an-1m-pv1", 3, procs, False)]
    if name == "official":
        return [("main", "an-10k-pv1", ALL, procs, False),
                ("main", "an-10k-pv4", ALL, procs, False),
                ("main", "av-10k", 20, procs, False),
                ("main", "an-1m-pv1", 16, procs, False)]
    raise SystemExit("unknown plan " + name)


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def cpu_model():
    try:
        system = platform.system()
        if system == "Linux":
            with open("/proc/cpuinfo") as f:
                text = f.read()
            for line in text.splitlines():
                if line.startswith("model name"):
                    return line.split(":", 1)[1].strip()
            out = subprocess.run(["lscpu"], stdout=subprocess.PIPE, universal_newlines=True).stdout
            for line in out.splitlines():
                if line.startswith("Model name"):
                    return line.split(":", 1)[1].strip()
        if system == "Darwin":
            return subprocess.run(["sysctl", "-n", "machdep.cpu.brand_string"],
                                  stdout=subprocess.PIPE, universal_newlines=True).stdout.strip()
        if system == "Windows":
            out = subprocess.run(["powershell", "-NoProfile", "-Command",
                                  "(Get-CimInstance Win32_Processor | Select-Object -First 1).Name"],
                                 stdout=subprocess.PIPE, universal_newlines=True).stdout
            return out.strip()
    except Exception as e:  # facts only; never fail the run on them
        return "unknown (%s)" % e
    return platform.processor() or "unknown"


def parse_kv(text, label):
    for line in text.splitlines():
        if line.strip().startswith(label):
            return line.split(":", 1)[1].strip()
    return None


def engine_facts(exe, prefix, plan):
    facts = {}
    code, out, err, _ = run_command(exe, ["compiler"], prefix)
    facts["compiler"] = [l for l in out.splitlines() if l.strip()]
    facts["dispatched_arch"] = parse_kv(out, "Compilation architecture")
    code, out, err, seconds = run_command(exe, ["bench"], prefix)
    facts["bench_nodes"] = int(parse_kv(err, "Nodes searched") or -1)
    facts["bench_nps"] = int(parse_kv(err, "Nodes/second") or -1)
    facts["bench_ms"] = int(parse_kv(err, "Total time (ms)") or -1)
    facts["bench_wall_seconds"] = round(seconds, 3)
    facts["bench_exit"] = code
    with Engine(exe, prefix=prefix) as eng:
        eng.search("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", [], 1000)
        facts["engine_id"] = eng.id_name
        facts["info_strings"] = eng.info_strings
    if plan != "emu":
        # Exported outside the results folder: the net is never uploaded.
        work = tempfile.mkdtemp(prefix="sfnet-")
        net = os.path.join(work, "exported.nnue")
        code, out, err, _ = run_command(exe, ["export_net", net], prefix)
        if os.path.exists(net):
            facts["exported_net_sha256"] = sha256_file(net)
            facts["exported_net_size"] = os.path.getsize(net)
        else:
            facts["exported_net_sha256"] = None
            facts["export_output"] = (out + err).strip()[-400:]
        shutil.rmtree(work, ignore_errors=True)
    return facts


class Stats:
    def __init__(self):
        self.lock = threading.Lock()
        self.search_seconds = []
        self.newgame_seconds = []
        self.start_seconds = []
        self.overshoot = []
        self.early = 0
        self.nodes = 0
        self.errors = []

    def add_search(self, result, limit):
        with self.lock:
            self.search_seconds.append(result["search_seconds"])
            self.newgame_seconds.append(result["newgame_seconds"])
            if result["lines"] and "nodes" in result["lines"][0]:
                n = result["lines"][0]["nodes"]
                self.nodes += n
                if n >= limit:
                    self.overshoot.append(n - limit)
                else:  # ended before the budget: depth limit reached (tiny trees, forced mates)
                    self.early += 1

    def add_start(self, seconds):
        with self.lock:
            self.start_seconds.append(seconds)


def summary(values, digits=6):
    if not values:
        return None
    s = sorted(values)
    return {"n": len(s), "min": round(s[0], digits), "median": round(statistics.median(s), digits),
            "mean": round(statistics.mean(s), digits), "p99": round(s[int(0.99 * (len(s) - 1))], digits),
            "max": round(s[-1], digits)}


def run_one(args, prefix, positions, variant, config, procs, fresh, timeout):
    mode, nodes, multipv = CONFIGS[config]
    stats = Stats()
    out = [None] * len(positions)
    work = queue.Queue()
    for i in range(len(positions)):
        work.put(i)

    def spawn():
        e = Engine(args.engine, multipv=multipv, prefix=prefix, low_priority=args.low_priority,
                   search_timeout=timeout)
        stats.add_start(e.start_seconds)
        return e

    def searched(eng, fen, moves):
        if fresh:
            with spawn() as e:
                r = e.search(fen, moves, nodes)
        else:
            r = eng.search(fen, moves, nodes)
        stats.add_search(r, nodes)
        return r

    def unit(eng, pos):
        fen, moves = pos["fen"], pos["moves"]
        if mode == "analyse":
            return records.analyse_record(pos, nodes, multipv, searched(eng, fen, moves))
        legal = eng.legal_moves(fen, moves)
        entries = [records.action_value_entry(m, searched(eng, fen, moves + [m])) for m in legal]
        terminal = None
        if not legal:
            terminal = records.terminal_of(searched(eng, fen, moves))
        return records.action_values_record(pos, nodes, legal, entries, terminal)

    def worker():
        eng = None
        while True:
            try:
                i = work.get_nowait()
            except queue.Empty:
                break
            for attempt in (1, 2):
                try:
                    if eng is None:
                        eng = spawn()
                    out[i] = unit(eng, positions[i])
                    break
                except (EngineError, OSError) as e:
                    with stats.lock:
                        stats.errors.append({"id": positions[i]["id"], "attempt": attempt,
                                             "error": str(e)[-600:]})
                    try:
                        eng.close()
                    except Exception:
                        pass
                    eng = None
        if eng is not None:
            eng.close()

    t0 = time.perf_counter()
    threads = [threading.Thread(target=worker) for _ in range(procs)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    wall = time.perf_counter() - t0

    folder = os.path.join(args.out, args.cell, "records", variant)
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, config + ".jsonl")
    with open(path, "w", encoding="ascii", newline="\n") as f:
        for rec in out:
            if rec is not None:
                f.write(records.dumps(rec) + "\n")
    search_total = sum(stats.search_seconds)
    return {"variant": variant, "config": config, "mode": mode, "nodes_limit": nodes,
            "multipv": multipv, "positions": len(positions),
            "records": sum(1 for r in out if r is not None), "procs": procs, "fresh": fresh,
            "searches": len(stats.search_seconds), "nodes_total": stats.nodes,
            "wall_seconds": round(wall, 3), "search_seconds_total": round(search_total, 3),
            "nps_aggregate": int(stats.nodes / wall) if wall else None,
            "nps_per_process": int(stats.nodes / search_total) if search_total else None,
            "overshoot": summary(stats.overshoot, 1), "ended_below_budget": stats.early,
            "newgame_seconds": summary(stats.newgame_seconds),
            "process_start_seconds": summary(stats.start_seconds),
            "errors": stats.errors, "sha256": sha256_file(path)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--engine", required=True)
    ap.add_argument("--cell", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--positions", required=True)
    ap.add_argument("--sfbench")
    ap.add_argument("--plan", default="native")
    ap.add_argument("--prefix", default="", help="emulator command, arguments separated by |")
    ap.add_argument("--procs", type=int, default=os.cpu_count() or 1)
    ap.add_argument("--build-json")
    ap.add_argument("--only", default="", help="comma-separated variant:config filter")
    ap.add_argument("--low-priority", action="store_true")
    args = ap.parse_args()

    prefix = [p for p in args.prefix.split("|") if p] if args.prefix else []
    positions = load_golden(args.positions)
    if args.sfbench:
        positions += load_sfbench(args.sfbench)
    cell_dir = os.path.join(args.out, args.cell)
    os.makedirs(cell_dir, exist_ok=True)
    emulated = bool(prefix) and os.path.basename(prefix[0]).lower() not in ("arch",)
    timeout = 7200 if emulated else 600

    facts = {"cell": args.cell, "plan": args.plan, "prefix": prefix[1:] if prefix else [],
             "emulator": os.path.basename(prefix[0]) if prefix else None,
             "os": platform.platform(), "machine": platform.machine(), "cpu": cpu_model(),
             "logical_cpus": os.cpu_count(), "procs": args.procs, "python": platform.python_version(),
             "engine_sha256": sha256_file(args.engine), "engine_size": os.path.getsize(args.engine),
             "positions_file_sha256": sha256_file(args.positions),
             "positions": len(positions)}
    if args.build_json and os.path.exists(args.build_json):
        with open(args.build_json) as f:
            facts["build"] = json.load(f)
    t0 = time.perf_counter()
    facts.update(engine_facts(args.engine, prefix, args.plan))
    facts["facts_seconds"] = round(time.perf_counter() - t0, 1)
    with open(os.path.join(cell_dir, "facts.json"), "w", newline="\n") as f:
        json.dump(facts, f, indent=1, sort_keys=True)
    print(json.dumps(facts, indent=1, sort_keys=True), flush=True)

    only = set(x for x in args.only.split(",") if x)
    runs = []
    for variant, config, first, procs, fresh in plan_of(args.plan, args.procs):
        if only and (variant + ":" + config) not in only:
            continue
        subset = positions if first is ALL else positions[:first]
        m = run_one(args, prefix, subset, variant, config, procs, fresh, timeout)
        runs.append(m)
        print("%-7s %-11s pos=%-4d procs=%-2d searches=%-6d wall=%7.1fs nps/proc=%s agg=%s err=%d"
              % (variant, config, m["positions"], procs, m["searches"], m["wall_seconds"],
                 m["nps_per_process"], m["nps_aggregate"], len(m["errors"])), flush=True)
        with open(os.path.join(cell_dir, "metrics.json"), "w", newline="\n") as f:
            json.dump({"cell": args.cell, "runs": runs}, f, indent=1, sort_keys=True)


if __name__ == "__main__":
    main()
