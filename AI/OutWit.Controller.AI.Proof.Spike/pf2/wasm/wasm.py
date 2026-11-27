"""PF-2 spike: the Rust kernels (nanoda, con-ron) built for wasm32-wasip1 and run by the wasmtime CLI
(the version our shared wasm host pins), against the same kernels as Lean 4.36.0 ships them.

    wasm.py run --wasmtime <exe> --root <lean root> --export <dir>/<file>.ndjson
                --nanoda <nanoda.wasm> --conron <con-ron.wasm> --cases a,b,... --out <record.json>
                [--cap-min 60] [--memory-gib 0] [--label L]

The guest sees one folder: the export's own, preopened as /job (`--dir <dir>::/job`); nothing
else of the host's file system, no network (WASI preview 1 has no sockets unless granted), no
processes. Cases:
  native-nanoda-1, native-nanoda-4, native-conron-1, native-conron   the shipped kernels
  wasm-nanoda, wasm-conron                    the wasm builds, one thread (default runtime limits)
  wasm-nanoda-fuel, wasm-conron-fuel          with fuel metering (a deterministic step budget)
  wasm-nanoda-epoch, wasm-conron-epoch        with an epoch deadline (a wall budget)
  wasm-nanoda-memcap                          linear memory capped at 256 MiB: must trap
  wasm-nanoda-fuelcap                         a fuel budget far too small: must trap
  wasm-nanoda-escape                          the config names a host file outside /job: must fail
  wasm-nanoda-stack, wasm-conron-stack        with a larger wasm stack (64 MiB)
  wasm-conron-big, wasm-conron-big-async      con-ron linked with a 512 MiB shadow stack, a 256 MiB wasm stack
  wasm-nanoda-threads-1, wasm-nanoda-threads-4    nanoda for wasm32-wasip1-threads under wasi-threads
"""
from __future__ import annotations

import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(HERE)), "pf1"))

from tree import run_tree  # noqa: E402
from common import lean_env, lower_priority, tool, write_json  # noqa: E402

FUEL = str(1 << 62)
STANDARD_AXIOMS = ["propext", "Quot.sound", "Classical.choice"]


def nanoda_config(path: str, export_path: str, threads: int) -> None:
    write_json(path, {"use_stdin": False, "export_file_path": export_path, "permitted_axioms": STANDARD_AXIOMS,
                      "unpermitted_axiom_hard_error": False, "num_threads": threads, "nat_extension": True,
                      "string_extension": True})


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("step", choices=["run"])
    parser.add_argument("--wasmtime", required=True)
    parser.add_argument("--root", required=True)
    parser.add_argument("--export", required=True)
    parser.add_argument("--nanoda", required=True)
    parser.add_argument("--conron", required=True)
    parser.add_argument("--cases", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--cap-min", type=float, default=60)
    parser.add_argument("--memory-gib", type=float, default=0)
    parser.add_argument("--label", default="")
    parser.add_argument("--conron-big", default="", help="con-ron linked with a larger shadow stack (optional)")
    parser.add_argument("--nanoda-threads", default="", help="nanoda for wasm32-wasip1-threads (optional)")
    parser.add_argument("--native-nanoda", default="", help="a native build of the same source (optional)")
    parser.add_argument("--native-conron", default="", help="a native build of the same source (optional)")
    options = parser.parse_args()
    lower_priority()
    root = os.path.abspath(options.root)
    export = os.path.abspath(options.export)
    options.nanoda = os.path.abspath(options.nanoda)
    options.conron = os.path.abspath(options.conron)
    options.wasmtime = os.path.abspath(options.wasmtime)
    if options.native_nanoda:
        options.native_nanoda = os.path.abspath(options.native_nanoda)
    if options.conron_big:
        options.conron_big = os.path.abspath(options.conron_big)
    if options.nanoda_threads:
        options.nanoda_threads = os.path.abspath(options.nanoda_threads)
    if options.native_conron:
        options.native_conron = os.path.abspath(options.native_conron)
    job = os.path.dirname(export)
    name = os.path.basename(export)
    guest = f"/job/{name}"
    record = json.load(open(options.out, encoding="utf-8")) if os.path.isfile(options.out) else {}
    record.setdefault("label", options.label)
    record.setdefault("export", name)
    record.setdefault("export_bytes", os.path.getsize(export))
    record.setdefault("cases", {})
    nanoda_config(os.path.join(job, "pf2-native-1.json"), export, 1)
    nanoda_config(os.path.join(job, "pf2-native-4.json"), export, 4)
    nanoda_config(os.path.join(job, "pf2-wasm.json"), guest, 1)
    nanoda_config(os.path.join(job, "pf2-wasm-4.json"), guest, 4)
    # A config whose export path leaves the preopened folder: the guest must not reach it.
    outside = os.path.join(os.path.dirname(job), "pf2-outside-secret.txt")
    with open(outside, "w", encoding="utf-8") as handle:
        handle.write("a file outside the job folder\n")
    nanoda_config(os.path.join(job, "pf2-wasm-escape.json"), "/job/../pf2-outside-secret.txt", 1)
    host_escape = os.path.join(job, "pf2-wasm-escape-abs.json")
    nanoda_config(host_escape, outside.replace("\\", "/"), 1)
    wt = [options.wasmtime, "run", "--dir", f"{job}::/job"]
    cases = {
        "native-nanoda-1": [tool(root, "nanoda_bin"), os.path.join(job, "pf2-native-1.json")],
        "native-nanoda-4": [tool(root, "nanoda_bin"), os.path.join(job, "pf2-native-4.json")],
        "native-conron-1": [tool(root, "con-ron"), "--jobs=1", export],
        "native-conron": [tool(root, "con-ron"), export],
        "wasm-nanoda": wt + [options.nanoda, "/job/pf2-wasm.json"],
        "wasm-nanoda-fuel": wt + ["-W", f"fuel={FUEL}", options.nanoda, "/job/pf2-wasm.json"],
        "wasm-nanoda-epoch": wt + ["-W", f"timeout={int(options.cap_min * 60)}s", options.nanoda, "/job/pf2-wasm.json"],
        "wasm-nanoda-stack": wt + ["-W", "max-wasm-stack=67108864", options.nanoda, "/job/pf2-wasm.json"],
        "wasm-nanoda-memcap": wt + ["-W", "max-memory-size=268435456", options.nanoda, "/job/pf2-wasm.json"],
        "wasm-nanoda-fuelcap": wt + ["-W", "fuel=100000000", options.nanoda, "/job/pf2-wasm.json"],
        "wasm-nanoda-escape": wt + [options.nanoda, "/job/pf2-wasm-escape.json"],
        "wasm-nanoda-escape-abs": wt + [options.nanoda, "/job/pf2-wasm-escape-abs.json"],
        "wasm-conron": wt + [options.conron, "--jobs=1", guest],
        "wasm-conron-fuel": wt + ["-W", f"fuel={FUEL}", options.conron, "--jobs=1", guest],
        "wasm-conron-epoch": wt + ["-W", f"timeout={int(options.cap_min * 60)}s", options.conron, "--jobs=1", guest],
        "wasm-conron-stack": wt + ["-W", "max-wasm-stack=67108864", options.conron, "--jobs=1", guest],
    }
    if options.conron_big:
        big = ["-W", "max-wasm-stack=268435456"]
        cases["wasm-conron-big"] = wt + big + [options.conron_big, "--jobs=1", guest]
        cases["wasm-conron-big-async"] = wt + big + ["-W", "async-stack-size=285212672", options.conron_big, "--jobs=1", guest]
    if options.nanoda_threads:
        threads = ["-W", "threads=y", "-W", "shared-memory=y", "-S", "threads=y"]
        cases["wasm-nanoda-threads-1"] = wt + threads + [options.nanoda_threads, "/job/pf2-wasm.json"]
        cases["wasm-nanoda-threads-4"] = wt + threads + [options.nanoda_threads, "/job/pf2-wasm-4.json"]
    if options.native_nanoda:
        cases["own-native-nanoda-1"] = [options.native_nanoda, os.path.join(job, "pf2-native-1.json")]
    if options.native_conron:
        cases["own-native-conron-1"] = [options.native_conron, "--jobs=1", export]
    env = lean_env(root, None)
    for case in [c for c in options.cases.split(",") if c]:
        if case not in cases:
            record["cases"][case] = {"skipped": "unknown case"}
            continue
        print(f"--- {case}", flush=True)
        row = run_tree(cases[case], env=env, cwd=job, timeout=options.cap_min * 60,
                       memory_gib=options.memory_gib or None, stack_unlimited=True)
        row.pop("stdout_tail", None)
        row["command"] = " ".join(os.path.basename(c) if os.path.isabs(c) else c for c in cases[case])
        record["cases"][case] = row
        print(json.dumps({k: row.get(k) for k in ("exit", "ending", "wall_s", "tree_rss", "scope_anon")}),
              (row.get("says") or "")[-400:], flush=True)
        write_json(options.out, record)
    write_json(options.out, record)
    return 0


if __name__ == "__main__":
    sys.exit(main())
