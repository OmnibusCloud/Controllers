"""PF-0 spike: the two-stage split. Check and export on one machine, confirm on another.

    split.py export  --lean-root <root> --work <dir> --items items.jsonl --ids <file or list>
                     --out <results dir> --exports <dir> [--transfer-mb N] [--paranoid N]
    split.py confirm --lean-root <root> --work <dir> --items items.jsonl --exports <dir>
                     --out <results dir> [--sandbox]

`export` (the checking machine): for each chosen item the solution is elaborated as a module of
its own (`lean -o`), the declaration and everything it rests on are exported with the
toolchain's `leanexport` (NDJSON), and the same is done for the challenge. The pair is then
judged on the same machine with `lake comparator --challenge-from-export --solution-from-export`
and the kernel alone is timed with `leanchecker --from-export`. The solution export is
compressed into the exports folder (the artifact that travels).

`confirm` (another machine): elaborates and exports the challenge from the trusted text of the
items file, takes the travelled solution export, and runs `lake comparator` on the two exports.
Nothing of the solution's text is elaborated here: the programs that run are `lean` and
`leanexport` on the challenge, and `lake` with `leanchecker` on the exports.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
import time

from pf0lib import EXE, IS_LINUX, STANDARD_AXIOMS, lean_environment, read_jsonl, run_measured, sha256_file, write_json

# What `lake comparator` exports beside the theorem (Lake.Check: builtinTargets, primitiveTargets).
QUOT = ["Quot", "Quot.mk", "Quot.lift", "Quot.ind"]
PRIMITIVES = ["Nat.add", "Nat.sub", "Nat.mul", "Nat.pow", "Nat.gcd", "Nat.div", "Nat.mod", "Nat.beq",
              "Nat.ble", "Nat.land", "Nat.lor", "Nat.xor", "Nat.shiftLeft", "Nat.shiftRight",
              "String.ofList", "Char.ofNat", "List", "eagerReduce", "Nat", "String", "String.mk", "Char",
              "optParam", "autoParam", "semiOutParam", "outParam"]
AXIOMS = ["propext", "Quot.sound", "Classical.choice"]
assert sorted(AXIOMS) == sorted(STANDARD_AXIOMS)


class Stage:
    def __init__(self, options: argparse.Namespace):
        self.bin = os.path.join(os.path.abspath(options.lean_root), "bin")
        self.work = os.path.abspath(options.work)
        with open(os.path.join(self.work, "leanpath.json"), encoding="utf-8") as handle:
            self.lean_path = json.load(handle)
        self.max_heartbeats = options.max_heartbeats

    def tool(self, name: str) -> str:
        return os.path.join(self.bin, name + EXE)

    def module(self, folder: str, module: str, item: dict, text_key: str) -> dict:
        """Elaborates one text as the module `module` in `folder` and exports its theorem."""
        lines = ["import Mathlib", f"set_option maxHeartbeats {item.get('max_heartbeats') or self.max_heartbeats}"]
        lines += item.get("opens", [])
        if text_key == "solution":
            lines += item.get("solution_pre", [])
        lines.append(item[text_key])
        with open(os.path.join(folder, module + ".lean"), "w", encoding="utf-8", newline="\n") as handle:
            handle.write("\n".join(lines) + "\n")
        env = lean_environment(self.lean_path + [folder], bin_dir=self.bin)
        built = run_measured([self.tool("lean"), "-DElab.async=false", "-o", module + ".olean", module + ".lean"],
                             env=env, cwd=folder, timeout=900)
        row = {"elab_s": built["wall_s"], "elab_exit": built["exit"], "elab_peak_rss": built["peak_rss"]}
        if not os.path.isfile(os.path.join(folder, module + ".olean")):
            row["error"] = (built["stdout"] + built["stderr"])[-400:]
            return row
        row["olean_bytes"] = os.path.getsize(os.path.join(folder, module + ".olean"))
        export = os.path.join(folder, module.lower() + ".ndjson")
        targets = QUOT + [item["name"]] + AXIOMS + PRIMITIVES
        out = run_measured([self.tool("leanexport"), module, "--"] + targets, env=env, cwd=folder,
                           timeout=1800, stdout_path=export)
        row.update({"export_s": out["wall_s"], "export_exit": out["exit"], "export_peak_rss": out["peak_rss"],
                    "export_bytes": os.path.getsize(export) if os.path.isfile(export) else 0})
        if out["exit"] != 0:
            row["error"] = out["stderr"][-400:]
            return row
        row["export_sha256"] = sha256_file(export)
        with open(export, "rb") as handle:
            row["export_lines"] = sum(1 for _ in handle)
        row["export"] = export
        return row

    def judge(self, folder: str, item: dict, challenge: str, solution: str, sandbox: bool,
              paranoid: bool = False) -> dict:
        config = os.path.join(folder, "comparator.json")
        write_json(config, {"challenge_module": "Challenge", "solution_module": "Solution",
                            "theorem_names": [item["name"]], "permitted_axioms": AXIOMS})
        cmd = [self.tool("lake"), "comparator", "--config", config, "--challenge-from-export", challenge,
               "--solution-from-export", solution]
        if not sandbox:
            cmd.append("--inadvisably-no-sandbox")
        if paranoid:
            cmd.append("--paranoid")
        env = lean_environment([], bin_dir=self.bin)
        out = run_measured(cmd, env=env, cwd=folder, timeout=3600)
        text = (out["stdout"] + "\n" + out["stderr"]).strip()
        return {"exit": out["exit"], "wall_s": out["wall_s"], "user_s": out["user_s"], "sys_s": out["sys_s"],
                "peak_rss": out["peak_rss"], "peak_private": out.get("peak_private"),
                "says": " / ".join(line.strip() for line in text.splitlines() if line.strip())[-500:]}

    def kernel(self, folder: str, solution: str) -> dict:
        env = lean_environment([], bin_dir=self.bin)
        out = run_measured([self.tool("leanchecker"), "--from-export", solution], env=env, cwd=folder, timeout=3600)
        return {"exit": out["exit"], "wall_s": out["wall_s"], "user_s": out["user_s"], "peak_rss": out["peak_rss"],
                "says": (out["stdout"] + out["stderr"]).strip()[-200:]}


def compress(source: str, target: str) -> dict:
    from compression import zstd
    started = time.monotonic()
    with open(source, "rb") as reader, zstd.open(target, "wb", level=3) as writer:
        shutil.copyfileobj(reader, writer, 1 << 20)
    return {"zst_bytes": os.path.getsize(target), "zst_s": round(time.monotonic() - started, 2)}


def decompress(source: str, target: str) -> float:
    from compression import zstd
    started = time.monotonic()
    with zstd.open(source, "rb") as reader, open(target, "wb") as writer:
        shutil.copyfileobj(reader, writer, 1 << 20)
    return round(time.monotonic() - started, 2)


def wanted_ids(text: str) -> list:
    if os.path.isfile(text):
        with open(text, encoding="utf-8") as handle:
            return [line.strip() for line in handle if line.strip()]
    return [part for part in text.split(",") if part]


def export(options: argparse.Namespace) -> int:
    stage = Stage(options)
    items = {item["id"]: item for item in read_jsonl(options.items)}
    ids = [name for name in wanted_ids(options.ids) if name in items]
    os.makedirs(options.exports, exist_ok=True)
    budget = options.transfer_mb << 20
    rows = []
    for index, name in enumerate(ids):
        item = items[name]
        folder = os.path.join(stage.work, "split", name)
        os.makedirs(folder, exist_ok=True)
        row = {"id": name, "expect": item.get("expect")}
        row["solution"] = stage.module(folder, "Solution", item, "solution")
        row["challenge"] = stage.module(folder, "Challenge", item, "challenge")
        solution = row["solution"].pop("export", None)
        challenge = row["challenge"].pop("export", None)
        if solution and challenge:
            row["comparator"] = stage.judge(folder, item, challenge, solution, sandbox=False)
            row["kernel"] = stage.kernel(folder, solution)
            if index < options.paranoid:
                row["paranoid"] = stage.judge(folder, item, challenge, solution, sandbox=False, paranoid=True)
            target = os.path.join(options.exports, name + ".solution.ndjson.zst")
            row["transfer"] = compress(solution, target)
            if row["transfer"]["zst_bytes"] > budget:
                os.remove(target)
                row["transfer"]["left_out"] = True
            else:
                budget -= row["transfer"]["zst_bytes"]
                write_json(os.path.join(options.exports, name + ".json"),
                           {"id": name, "solution_sha256": row["solution"]["export_sha256"],
                            "challenge_sha256": row["challenge"]["export_sha256"],
                            "comparator_exit": row["comparator"]["exit"]})
        rows.append(row)
        print(json.dumps(row), flush=True)
        shutil.rmtree(folder, ignore_errors=True)
    write_json(os.path.join(options.out, "split-export.json"), rows)
    return 0


def confirm(options: argparse.Namespace) -> int:
    stage = Stage(options)
    items = {item["id"]: item for item in read_jsonl(options.items)}
    rows = []
    names = sorted(entry[:-len(".json")] for entry in os.listdir(options.exports) if entry.endswith(".json"))
    for name in names:
        if name not in items:
            continue
        item = items[name]
        with open(os.path.join(options.exports, name + ".json"), encoding="utf-8") as handle:
            sent = json.load(handle)
        folder = os.path.join(stage.work, "confirm", name)
        os.makedirs(folder, exist_ok=True)
        row = {"id": name, "expect": item.get("expect"), "sender_comparator_exit": sent["comparator_exit"]}
        solution = os.path.join(folder, "solution.ndjson")
        row["unpack_s"] = decompress(os.path.join(options.exports, name + ".solution.ndjson.zst"), solution)
        row["solution_bytes"] = os.path.getsize(solution)
        row["solution_sha256_matches"] = sha256_file(solution) == sent["solution_sha256"]
        row["challenge"] = stage.module(folder, "Challenge", item, "challenge")
        challenge = row["challenge"].pop("export", None)
        if challenge:
            row["challenge_export_same_as_sender"] = row["challenge"]["export_sha256"] == sent["challenge_sha256"]
            row["comparator"] = stage.judge(folder, item, challenge, solution, sandbox=False)
            if options.sandbox and IS_LINUX:
                row["comparator_sandboxed"] = stage.judge(folder, item, challenge, solution, sandbox=True)
        rows.append(row)
        print(json.dumps(row), flush=True)
        shutil.rmtree(folder, ignore_errors=True)
    write_json(os.path.join(options.out, f"split-confirm-{options.label}.json"), rows)
    return 0 if rows else 1


def choose(options: argparse.Namespace) -> int:
    """The items of the golden set that go through the split: accepted ones spread evenly over
    the set (the heaviest among them), and made ones that elaborate without an error but must
    not be confirmed (a `sorry`, another statement, an added axiom, a native decision)."""
    items = read_jsonl(options.items)
    accepted = sorted(item["id"] for item in items if item.get("class") == "accepted")
    step = max(1, len(accepted) // options.accepted)
    chosen = accepted[::step][:options.accepted]
    for rule, count in (("budget_edge", 4), ("sorry", 3), ("mismatch", 4), ("axiom", 3), ("fact:native_decide", 2),
                        ("fact:decide", 2)):
        chosen += sorted(item["id"] for item in items if item.get("made") == rule)[:count]
    os.makedirs(os.path.dirname(os.path.abspath(options.ids)), exist_ok=True)
    with open(options.ids, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(chosen) + "\n")
    print(f"{len(chosen)} items chosen for the split")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("stage", choices=["choose", "export", "confirm"])
    parser.add_argument("--lean-root", default="")
    parser.add_argument("--work", default="")
    parser.add_argument("--items", required=True)
    parser.add_argument("--ids", default="")
    parser.add_argument("--exports", default="")
    parser.add_argument("--out", default="")
    parser.add_argument("--max-heartbeats", type=int, default=400000)
    parser.add_argument("--transfer-mb", type=int, default=400)
    parser.add_argument("--paranoid", type=int, default=0)
    parser.add_argument("--accepted", type=int, default=40)
    parser.add_argument("--sandbox", action="store_true")
    parser.add_argument("--label", default="local")
    options = parser.parse_args()
    if options.stage == "choose":
        return choose(options)
    os.makedirs(options.out, exist_ok=True)
    return export(options) if options.stage == "export" else confirm(options)


if __name__ == "__main__":
    sys.exit(main())
