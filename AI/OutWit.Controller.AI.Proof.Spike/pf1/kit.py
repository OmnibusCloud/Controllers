"""PF-1 spike: a replay kit on the submitter's side (spec section 12): the project built at its
pinned commit, its main theorems exported with their whole closure, the trusted challenge exported
from the official build of the declared Mathlib revision, and the variants the statement check must
refuse.

    kit.py build    --kit R1 --root <lean root> --work <dir> --out <dir>
    kit.py export   --kit R1 --root <lean root> --work <dir> --out <dir> --artifacts <dir>
    kit.py variants --kit R1 --root <lean root> --work <dir> --out <dir> --artifacts <dir>
    kit.py driver   --kit R1 --root <lean root> --work <dir> --out <dir> --driver <exe>
    kit.py judge    --kit R1 --root <pinned lean root> --work <dir> --out <dir>

`build` clones the project at its commit, fetches Mathlib's official cache for the project's
Mathlib revision (`lake exe cache get`) and builds the project (`lake build`). `export` runs the
toolchain's `leanexport` on the solution module (the theorems of the project's comparator record,
and the main theorem alone) and on the challenge module, and packs the exports with zstd.
`variants` makes what must be refused: a challenge worded differently, a doctored definition in a
copy of the export (its reducibility hints; its value), and for R2 the theorem that rests on
`sorry`. `driver` runs the fast driver on each; `judge` runs `lake comparator` on the refusals
(they end before any kernel runs).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys

from common import (STANDARD_AXIOMS, capped, comparator_command, comparator_config, compress, export_targets,
                    lean_env, load_pins, read_export_summary, run_measured, sha256_file, tool, tree_bytes,
                    write_json)
import doctor

HERE = os.path.dirname(os.path.abspath(__file__))


class Kit:
    def __init__(self, options: argparse.Namespace):
        pins = load_pins()
        k = options.kit
        self.name = k
        self.repo = pins[f"{k}_REPO"]
        self.commit = pins[f"{k}_COMMIT"]
        self.lean = pins[f"{k}_LEAN"]
        self.mathlib = pins[f"{k}_MATHLIB"]
        self.build_targets = pins[f"{k}_BUILD"].split()
        self.solution = pins[f"{k}_SOLUTION"]
        self.challenge = pins[f"{k}_CHALLENGE"]
        self.theorems = pins[f"{k}_THEOREMS"].split(",")
        self.main = pins[f"{k}_MAIN"]
        self.negative_file = pins.get(f"{k}_NEGATIVE_FILE")
        self.root = os.path.abspath(options.root)
        self.work = os.path.abspath(options.work)
        self.out = os.path.abspath(options.out)
        self.project = os.path.join(self.work, "project")
        self.exports = os.path.join(self.work, "exports")
        os.makedirs(self.out, exist_ok=True)
        os.makedirs(self.exports, exist_ok=True)

    def env(self, lean_path: list | None = None) -> dict:
        env = lean_env(self.root, lean_path)
        env["MATHLIB_CACHE_DIR"] = os.path.join(self.work, "mathlib-cache")
        env["GIT_TERMINAL_PROMPT"] = "0"
        return env

    def lake(self, args: list, timeout: float = 4 * 3600, stdout_path: str | None = None) -> dict:
        return run_measured([tool(self.root, "lake")] + args, env=self.env(), cwd=self.project, timeout=timeout,
                            stdout_path=stdout_path)

    def lean_path(self) -> list:
        """The project's search path as `lake env` reports it."""
        probe = "import json, os; print(json.dumps(os.environ.get('LEAN_PATH', '')))"
        row = self.lake(["env", sys.executable, "-c", probe], timeout=600)
        value = json.loads(row["stdout"].strip().splitlines()[-1])
        return [p for p in value.split(os.pathsep) if p]


def short(row: dict) -> dict:
    keep = ("exit", "wall_s", "user_s", "sys_s", "peak_rss", "peak_private", "killed")
    out = {key: row.get(key) for key in keep}
    text = ((row.get("stdout") or "") + "\n" + (row.get("stderr") or "")).strip().splitlines()
    out["tail"] = [line[:300] for line in text[-12:]]
    return out


def build(kit: Kit) -> int:
    record = {"kit": kit.name, "repo": kit.repo, "commit": kit.commit, "lean": kit.lean}
    if os.path.isdir(kit.project):
        shutil.rmtree(kit.project)
    os.makedirs(kit.project)
    for args in (["init", "-q"], ["remote", "add", "origin", kit.repo],
                 ["fetch", "-q", "--depth", "1", "origin", kit.commit], ["checkout", "-q", "FETCH_HEAD"]):
        subprocess.run(["git"] + args, cwd=kit.project, check=True)
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=kit.project, capture_output=True, text=True).stdout.strip()
    record["head"] = head
    if head != kit.commit:
        raise SystemExit(f"{kit.repo} is at {head}, pinned {kit.commit}")
    with open(os.path.join(kit.project, "lean-toolchain"), encoding="utf-8") as handle:
        record["lean_toolchain"] = handle.read().strip()
    source_bytes = 0
    lean_files = 0
    for folder, dirs, files in os.walk(kit.project):
        dirs[:] = [d for d in dirs if d not in (".git", ".lake")]
        for name in files:
            if name.endswith(".lean"):
                lean_files += 1
                source_bytes += os.path.getsize(os.path.join(folder, name))
    record["lean_files"] = lean_files
    record["lean_source_bytes"] = source_bytes

    print("--- lake exe cache get", flush=True)
    row = kit.lake(["exe", "cache", "get"], timeout=3600)
    record["cache_get"] = short(row)
    print("\n".join(record["cache_get"]["tail"]), flush=True)
    if row["exit"] != 0:
        write_json(os.path.join(kit.out, "build.json"), record)
        return 1
    manifest = json.load(open(os.path.join(kit.project, "lake-manifest.json"), encoding="utf-8"))
    record["packages"] = {p["name"]: p.get("rev") for p in manifest.get("packages", [])}
    if record["packages"].get("mathlib") != kit.mathlib:
        print(f"warning: the manifest names Mathlib {record['packages'].get('mathlib')}, pinned {kit.mathlib}")
    cache = os.path.join(kit.work, "mathlib-cache")
    record["cache_bytes"], record["cache_files"] = tree_bytes(cache) if os.path.isdir(cache) else (0, 0)

    print(f"--- lake build {' '.join(kit.build_targets)}", flush=True)
    row = kit.lake(["build"] + kit.build_targets, timeout=5 * 3600)
    record["build"] = short(row)
    print("\n".join(record["build"]["tail"]), flush=True)
    record["build_dir_bytes"] = tree_bytes(os.path.join(kit.project, ".lake", "build"))[0]
    write_json(os.path.join(kit.out, "build.json"), record)
    return 0 if row["exit"] == 0 else 1


def export_one(kit: Kit, module: str, names: list, target: str, lean_path: list | None = None,
               cwd: str | None = None) -> dict:
    env = kit.env(lean_path) if lean_path is not None else None
    if env is None:
        cmd = [tool(kit.root, "lake"), "env", tool(kit.root, "leanexport"), module, "--"] + names
        env = kit.env()
    else:
        cmd = [tool(kit.root, "leanexport"), module, "--"] + names
    row = run_measured(cmd, env=env, cwd=cwd or kit.project, timeout=3 * 3600, stdout_path=target)
    out = short(row)
    if row["exit"] == 0 and os.path.isfile(target):
        out.update(read_export_summary(target))
    return out


def export(kit: Kit, artifacts: str) -> int:
    os.makedirs(artifacts, exist_ok=True)
    record = {"kit": kit.name, "theorems": kit.theorems, "main": kit.main}
    plan = [("solution", kit.solution, export_targets(kit.theorems)),
            ("main", kit.solution, export_targets([kit.main])),
            ("challenge", kit.challenge, export_targets(kit.theorems))]
    for label, module, names in plan:
        target = os.path.join(kit.exports, f"{label}.ndjson")
        print(f"--- leanexport {module} ({label})", flush=True)
        record[label] = export_one(kit, module, names, target)
        print(json.dumps({k: record[label].get(k) for k in ("exit", "wall_s", "peak_rss", "bytes", "lines", "meta")}),
              flush=True)
        write_json(os.path.join(kit.out, "export.json"), record)
    # Packed for the way: zstd at the level a submitter would use, and at the highest.
    for label in ("solution", "challenge"):
        source = os.path.join(kit.exports, f"{label}.ndjson")
        if not os.path.isfile(source):
            continue
        record[label]["zstd3"] = compress(source, os.path.join(artifacts, f"{label}.ndjson.zst"), 3)
        if label == "solution":
            record[label]["zstd19"] = compress(source, os.path.join(kit.work, "solution.19.zst"), 19)
            os.remove(os.path.join(kit.work, "solution.19.zst"))
    main = os.path.join(kit.exports, "main.ndjson")
    if os.path.isfile(main):
        record["main"]["zstd3"] = compress(main, os.path.join(kit.work, "main.zst"), 3)
        os.remove(os.path.join(kit.work, "main.zst"))
    write_json(os.path.join(kit.out, "export.json"), record)
    write_json(os.path.join(artifacts, "exports.json"),
               {label: {"sha256": record[label].get("sha256"), "bytes": record[label].get("bytes"),
                        "meta": record[label].get("meta")} for label in ("solution", "challenge", "main")
                if label in record})
    return 0 if record["solution"].get("exit") == 0 and record["challenge"].get("exit") == 0 else 1


def module_file(kit: Kit, module: str) -> str:
    return os.path.join(kit.project, *module.split(".")) + ".lean"


def compile_module(kit: Kit, folder: str, module: str, text: str, lean_path: list) -> dict:
    """Elaborates `text` as the module `module` in `folder` (with the project's search path)."""
    path = os.path.join(folder, *module.split(".")) + ".lean"
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)
    olean = path[:-5] + ".olean"
    rel = os.path.relpath(path, folder)
    row = run_measured([tool(kit.root, "lean"), "-o", os.path.relpath(olean, folder), rel],
                       env=kit.env(lean_path + [folder]), cwd=folder, timeout=3600)
    return short(row)


def variants(kit: Kit, artifacts: str) -> int:
    os.makedirs(artifacts, exist_ok=True)
    lean_path = kit.lean_path()
    record = {"kit": kit.name}
    folder = os.path.join(kit.work, "variants")
    os.makedirs(folder, exist_ok=True)
    challenge_text = open(module_file(kit, kit.challenge), encoding="utf-8").read()

    # A wording that differs: the same theorem, its statement written another way.
    wording, how = reword(kit.name, challenge_text)
    record["wording_change"] = how
    module = kit.challenge.rsplit(".", 1)[0] + ".PF1Wording" if "." in kit.challenge else "PF1Wording"
    record["wording_compile"] = compile_module(kit, folder, module, wording, lean_path)
    if record["wording_compile"]["exit"] == 0:
        target = os.path.join(kit.exports, "wording.ndjson")
        record["wording_export"] = export_one(kit, module, export_targets(kit.theorems), target,
                                              lean_path=lean_path + [folder], cwd=folder)
        compress(target, os.path.join(artifacts, "wording.ndjson.zst"), 3)

    # A doctored definition in a copy of the export: one constant the statement uses.
    solution = os.path.join(kit.exports, "solution.ndjson")
    for mode in doctor.MODES[kit.name]:
        target = os.path.join(kit.exports, f"doctored-{mode['label']}.ndjson")
        record[f"doctored_{mode['label']}"] = doctor.doctor(solution, target, mode)
        compress(target, os.path.join(artifacts, f"doctored-{mode['label']}.ndjson.zst"), 3)

    # R2: the theorem whose proof the project's README says still uses `sorry`.
    if kit.negative_file:
        path = os.path.join(kit.project, kit.negative_file)
        text = open(path, encoding="utf-8").read()
        found = re.findall(r"^(?:theorem|lemma)\s+(carleson_hunt\S*)", text, flags=re.M)
        record["negative_names"] = found
        if found:
            name = found[0]
            namespace = re.findall(r"^namespace\s+(\S+)", text, flags=re.M)
            full = name if not namespace else f"{namespace[0]}.{name}"
            module = kit.negative_file[:-5].replace("/", ".")
            target = os.path.join(kit.exports, "negative.ndjson")
            # Tried with and without the namespace the file opens.
            for candidate in ([full, name] if full != name else [name]):
                record["negative_export"] = export_one(kit, module, export_targets([candidate]), target)
                record["negative_theorem"] = candidate
                if record["negative_export"].get("exit") == 0:
                    break
            if record["negative_export"].get("exit") == 0:
                compress(target, os.path.join(artifacts, "negative.ndjson.zst"), 3)
    write_json(os.path.join(kit.out, "variants.json"), record)
    return 0


def reword(kit: str, text: str) -> tuple[str, str]:
    """The challenge with one main statement written another way (the same mathematics)."""
    if kit == "R1":
        old, new = "Nat.card c < 2 * K ^ 12", "Nat.card c < K ^ 12 * 2"
    else:
        old, new = "(periodic_f : f.Periodic (2 * π))", "(periodic_f : f.Periodic (π * 2))"
    if old not in text:
        return text, f"pattern {old!r} not found: no change"
    return text.replace(old, new, 1), f"{old!r} -> {new!r} (first occurrence)"


def driver(kit: Kit, exe: str) -> int:
    lean_path = kit.lean_path()
    env = kit.env(lean_path)
    record = {"kit": kit.name}
    challenge = os.path.join(kit.exports, "challenge.ndjson")
    theorems = ",".join(kit.theorems)
    runs = [("accepted", "solution.ndjson", challenge, theorems, None)]
    for mode in doctor.MODES[kit.name]:
        runs.append((f"doctored_{mode['label']}", f"doctored-{mode['label']}.ndjson", challenge, theorems, None))
    if os.path.isfile(os.path.join(kit.exports, "wording.ndjson")):
        runs.append(("wording", "solution.ndjson", os.path.join(kit.exports, "wording.ndjson"), theorems, None))
    runs.append(("axioms_no_choice", "solution.ndjson", challenge, theorems, "propext,Quot.sound"))
    if os.path.isfile(os.path.join(kit.exports, "negative.ndjson")):
        variants_record = json.load(open(os.path.join(kit.out, "variants.json"), encoding="utf-8"))
        negative = os.path.join(kit.exports, "negative.ndjson")
        runs.append(("negative_sorry", "negative.ndjson", negative, variants_record["negative_theorem"], None))
    for label, solution, chal, names, axioms in runs:
        cmd = [exe, "fast", os.path.join(kit.exports, solution), "--challenge", chal, "--theorems", names,
               "--import", "Mathlib"]
        if axioms:
            cmd += ["--axioms", axioms]
        print(f"--- driver fast: {label}", flush=True)
        row = capped(cmd, env=env, cwd=kit.work, timeout=3 * 3600)
        row["result"] = row.pop("json", None)
        record[label] = row
        print(json.dumps({k: row.get(k) for k in ("exit", "wall_s", "peak_rss")}), (row["says"] or "")[-400:], flush=True)
        write_json(os.path.join(kit.out, "driver.json"), record)
    return 0


def judge(kit: Kit) -> int:
    """`lake comparator` (the pinned toolchain) on what must be refused before any kernel runs."""
    record = {"kit": kit.name}
    folder = os.path.join(kit.work, "judge")
    os.makedirs(folder, exist_ok=True)
    challenge = os.path.join(kit.exports, "challenge.ndjson")
    plain = comparator_config(os.path.join(folder, "comparator.json"), kit.theorems)
    no_choice = comparator_config(os.path.join(folder, "comparator-no-choice.json"), kit.theorems,
                                  ["propext", "Quot.sound"])
    runs = []
    for mode in doctor.MODES[kit.name]:
        runs.append((f"doctored_{mode['label']}", plain, challenge, f"doctored-{mode['label']}.ndjson"))
    if os.path.isfile(os.path.join(kit.exports, "wording.ndjson")):
        runs.append(("wording", plain, os.path.join(kit.exports, "wording.ndjson"), "solution.ndjson"))
    runs.append(("axioms_no_choice", no_choice, challenge, "solution.ndjson"))
    negative = os.path.join(kit.exports, "negative.ndjson")
    if os.path.isfile(negative):
        variants_record = json.load(open(os.path.join(kit.out, "variants.json"), encoding="utf-8"))
        config = comparator_config(os.path.join(folder, "comparator-negative.json"),
                                   [variants_record["negative_theorem"]])
        runs.append(("negative_sorry", config, negative, "negative.ndjson"))
    for label, config, chal, solution in runs:
        cmd = comparator_command(kit.root, config, chal, os.path.join(kit.exports, solution), sandbox=False)
        print(f"--- comparator: {label}", flush=True)
        # A refusal ends before the kernels; an hour is the cap if one does not.
        row = capped(cmd, env=lean_env(kit.root, []), cwd=folder, timeout=3600)
        record[label] = row
        print(json.dumps({k: row.get(k) for k in ("exit", "wall_s", "peak_rss")}), row["says"][-400:], flush=True)
        write_json(os.path.join(kit.out, "judge.json"), record)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("step", choices=["build", "export", "variants", "driver", "judge"])
    parser.add_argument("--kit", required=True, choices=["R1", "R2"])
    parser.add_argument("--root", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--artifacts", default="")
    parser.add_argument("--driver", default="")
    options = parser.parse_args()
    kit = Kit(options)
    if options.step == "build":
        return build(kit)
    if options.step == "export":
        return export(kit, options.artifacts)
    if options.step == "variants":
        return variants(kit, options.artifacts)
    if options.step == "driver":
        return driver(kit, options.driver)
    return judge(kit)


if __name__ == "__main__":
    sys.exit(main())
