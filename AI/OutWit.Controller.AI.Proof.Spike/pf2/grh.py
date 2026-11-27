"""PF-2 spike: the published exports of the shorter RH/GRH formalization (MIT report, pins.env),
fetched and checked, pre-checked, doctored, and judged by every kernel of the pinned toolchain.

    grh.py pin      KEY        (prints one pin)
    grh.py fetch    --what compact,readable,challenge --dest DIR --out RECORD.json [--keep-zst]
    grh.py precheck --tool "<command>" --dest DIR --what compact,readable,challenge --out RECORD.json
    grh.py doctor   --source compact.ndjson --variant hints-rh|true-primitive|removed|truncated
                    --target OUT.ndjson --out RECORD.json
    grh.py run      --root <lean root> --export S.ndjson [--challenge C.ndjson] --steps a,b,...
                    --out RECORD.json [--memory-gib 13] [--guard-gib 40] [--cap-min 120]
                    [--leche-compare EXE] [--ron-compare EXE] [--label L]

`fetch` downloads each export from the page, checks the compressed file against the page's
SHA256SUMS, decompresses it, and checks the result against the page's exports.sha256; every step
timed. `run` steps: leanchecker, leanchecker-paranoid, nanoda-<threads>, con-ron, con-leche,
lean4lean (each as `lake comparator` starts it), comparator (Lean's kernel only, the certificate's
command), comparator-bwrap (inside comparator's own bubblewrap), leche-compare and ron-compare
(the independent statement comparisons, `--compare CHALLENGE SOLUTION`). Every step's whole
process tree is measured (tree.py); a record is written after each step.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shlex
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "pf1"))

from common import (comparator_command, comparator_config, kernel_command, lean_env, lower_priority,  # noqa: E402
                    unpack_tar_zst, write_json)
from tree import IS_LINUX, run_tree  # noqa: E402


def load_pins() -> dict:
    pins = {}
    with open(os.path.join(HERE, "pins.env"), encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                pins[key.strip()] = value.strip()
    return pins


def sha256_of(path: str) -> tuple[str, float]:
    started = time.monotonic()
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        while True:
            chunk = handle.read(1 << 22)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest(), round(time.monotonic() - started, 1)


def decompress(source: str, target: str) -> tuple[float, str]:
    started = time.monotonic()
    try:
        from compression import zstd  # Python 3.14
        with zstd.open(source, "rb") as reader, open(target, "wb") as writer:
            shutil.copyfileobj(reader, writer, 1 << 22)
        how = "python compression.zstd"
    except ImportError:
        try:
            import zstandard
            with open(source, "rb") as raw, open(target, "wb") as writer:
                zstandard.ZstdDecompressor().copy_stream(raw, writer, read_size=1 << 22, write_size=1 << 22)
            how = "python zstandard"
        except ImportError:
            subprocess.run(["zstd", "-q", "-d", "-f", source, "-o", target], check=True)
            how = "zstd"
    return round(time.monotonic() - started, 1), how


def fetch(options) -> int:
    pins = load_pins()
    dest = os.path.abspath(options.dest)
    os.makedirs(dest, exist_ok=True)
    record = json.load(open(options.out, encoding="utf-8")) if os.path.isfile(options.out) else {}
    failed = False
    for what in [w for w in options.what.split(",") if w]:
        name, zst_sha, size, sha = pins[f"EXPORT__{what}"].split()
        url = f"{pins['GRH_PAGE']}/{name}"
        archive = os.path.join(dest, name)
        target = os.path.join(dest, name[:-len(".zst")])
        row = {"url": url}
        if os.path.isfile(target) and os.path.getsize(target) == int(size) and options.trust_present:
            row["present"] = True
        else:
            started = time.monotonic()
            subprocess.run(["curl", "-fsSL", "--retry", "5", "--retry-delay", "10", "--max-time", "5400",
                            "-o", archive, url], check=True)
            row["download_s"] = round(time.monotonic() - started, 1)
            row["zst_bytes"] = os.path.getsize(archive)
            row["download_mb_s"] = round(row["zst_bytes"] / 1e6 / max(row["download_s"], 0.1), 1)
            row["zst_sha256"], row["zst_sha256_s"] = sha256_of(archive)
            row["zst_sha256_matches_page"] = row["zst_sha256"] == zst_sha
            if not row["zst_sha256_matches_page"]:
                failed = True
                record[what] = row
                continue
            row["decompress_s"], row["decompress_how"] = decompress(archive, target)
            if not options.keep_zst:
                os.remove(archive)
        row["bytes"] = os.path.getsize(target)
        row["bytes_match_page"] = row["bytes"] == int(size)
        row["sha256"], row["sha256_s"] = sha256_of(target)
        row["sha256_matches_page"] = row["sha256"] == sha
        with open(target, "rb") as handle:
            row["meta"] = json.loads(handle.readline()).get("meta")
        failed = failed or not (row["sha256_matches_page"] and row["bytes_match_page"])
        record[what] = row
        print(what, json.dumps({k: v for k, v in row.items() if k != "meta"}), flush=True)
        write_json(options.out, record)
    write_json(options.out, record)
    return 1 if failed else 0


def precheck(options) -> int:
    pins = load_pins()
    dest = os.path.abspath(options.dest)
    record = json.load(open(options.out, encoding="utf-8")) if os.path.isfile(options.out) else {}
    tool = shlex.split(options.tool, posix=not sys.platform.startswith("win"))
    for what in [w for w in options.what.split(",") if w]:
        name, _, _, sha = pins[f"EXPORT__{what}"].split()
        plain = os.path.join(dest, name[:-len(".zst")])
        packed = os.path.join(dest, name)
        common = ["--lean-version", pins["GRH_LEAN"], "--theorems", pins["GRH_THEOREMS"]]
        runs = {
            "ndjson": [plain, "--expect-sha256", sha] + common,
            "ndjson-no-duplicates": [plain, "--no-duplicates"] + common,
        }
        if os.path.isfile(packed):
            runs["zst-one-pass"] = [packed, "--expect-sha256", sha] + common
        for label, args in runs.items():
            if not os.path.isfile(args[0]):
                continue
            print(f"--- precheck {what} {label}", flush=True)
            row = run_tree(tool + args, timeout=options.cap_min * 60, memory_gib=options.memory_gib or None)
            row["result"] = parse_last_json(row.pop("stdout_tail", ""))
            record[f"{what}/{label}"] = row
            print(json.dumps({k: row.get(k) for k in ("exit", "wall_s", "tree_rss", "ending")}),
                  json.dumps((row.get("result") or {}).get("verdict")), flush=True)
            write_json(options.out, record)
    return 0


def precheck_dir(options) -> int:
    """The pre-check over every export of a folder (PF-1's crafted inputs): how each ended."""
    tool = shlex.split(options.tool, posix=not sys.platform.startswith("win"))
    record = {}
    for name in sorted(os.listdir(options.dir)):
        if not name.endswith(".ndjson"):
            continue
        row = run_tree(tool + [os.path.join(options.dir, name), "--theorems", options.theorems], timeout=300)
        result = parse_last_json(row.pop("stdout_tail", "")) or {}
        record[name[:-len(".ndjson")]] = {"verdict": result.get("verdict"), "code": result.get("code"),
                                          "detail": result.get("detail"), "seconds": result.get("seconds"),
                                          "max_term_depth": result.get("max_term_depth"),
                                          "peak_working_set": (result.get("memory") or {}).get("peak_working_set"),
                                          "exit": row.get("exit")}
        print(name, json.dumps(record[name[:-len(".ndjson")]]), flush=True)
    write_json(options.out, record)
    return 0


def parse_last_json(text: str):
    start = text.rfind('{"tool"')
    if start < 0:
        return None
    depth = 0
    for index in range(start, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                try:
                    return json.loads(text[start:index + 1])
                except ValueError:
                    return None
    return None


# ---------------------------------------------------------------------------------------------
# Doctored copies (the comparator path is never optional)
# ---------------------------------------------------------------------------------------------

DOCTOR_TARGETS = {
    "hints-rh": {"def": "RiemannHypothesis"},
    "true-primitive": {"def": "PrimitiveDirichletLZerosOnLine", "thm": "LFunction_eq_zero_re_of_isPrimitive"},
    "removed": {"thm": "LFunction_eq_zero_re_of_isPrimitive"},
    "truncated": {},
}


def dumps(obj: dict) -> bytes:
    return (json.dumps(obj, separators=(",", ":"), ensure_ascii=False, sort_keys=True) + "\n").encode("utf-8")


def doctor(options) -> int:
    """One pass to find the lines, one to write the copy; every index stays dense and in order,
    and every changed declaration stays where it was (fold-order kernels need that)."""
    pins = load_pins()
    theorems = pins["GRH_THEOREMS"].split(",")
    variant = options.variant
    wanted = DOCTOR_TARGETS[variant]
    started = time.monotonic()
    names = [""]
    want_names = set(wanted.values()) | set(theorems) | {"True", "True.intro"}
    index_of = {}
    exprs = 0
    const_first = {}
    decl_lines = {}
    first_main = None
    with open(options.source, "rb") as handle:
        offset = 0
        for raw in handle:
            if raw.startswith(b'{"in":'):
                obj = json.loads(raw)
                if "str" in obj:
                    part, pre = obj["str"]["str"], obj["str"]["pre"]
                else:
                    part, pre = str(obj["num"]["i"]), obj["num"]["pre"]
                full = part if pre == 0 else f"{names[pre]}.{part}"
                names.append(full)
                if full in want_names:
                    index_of[full] = obj["in"]
            elif raw.startswith(b'{"const":'):
                obj = json.loads(raw)
                c = obj["const"]
                if not c["us"]:
                    nm = names[c["name"]]
                    if nm in ("True", "True.intro") and nm not in const_first:
                        const_first[nm] = obj["ie"]
                exprs += 1
            elif b'"ie":' in raw[:12] or raw.startswith(b'{"app":') or raw.startswith(b'{"bvar":') \
                    or raw.startswith(b'{"forallE":'):
                exprs += 1
            elif raw.startswith(b'{"def":') or raw.startswith(b'{"thm":'):
                obj = json.loads(raw)
                kind = "def" if raw.startswith(b'{"def":') else "thm"
                nm = names[obj[kind]["name"]]
                if kind == "thm" and nm in theorems and first_main is None:
                    first_main = offset
                if nm in wanted.values():
                    decl_lines[(kind, nm)] = (offset, obj, exprs)
            offset += len(raw)
    record = {"variant": variant, "source": os.path.basename(options.source), "scan_s": round(time.monotonic() - started, 1),
              "names": len(names), "exprs": exprs, "const_exprs": const_first}
    replace = {}
    drop = set()
    cut = None
    if variant == "hints-rh":
        offset, obj, _ = decl_lines[("def", wanted["def"])]
        before = obj["def"]["hints"]
        obj["def"]["hints"] = {"regular": before["regular"] + 1} if isinstance(before, dict) else {"regular": 1}
        record["change"] = {"constant": wanted["def"], "hints_before": before, "hints_after": obj["def"]["hints"]}
        replace[offset] = dumps(obj)
    elif variant == "true-primitive":
        d_offset, d_obj, d_exprs = decl_lines[("def", wanted["def"])]
        t_offset, t_obj, t_exprs = decl_lines[("thm", wanted["thm"])]
        true_e, intro_e = const_first.get("True"), const_first.get("True.intro")
        if true_e is None or intro_e is None or true_e >= d_exprs or intro_e >= t_exprs:
            raise SystemExit(f"no 'const True' / 'const True.intro' bound before the lines: {const_first}")
        record["change"] = {"definition": wanted["def"], "value_before": d_obj["def"]["value"], "value_after": true_e,
                            "theorem": wanted["thm"], "proof_before": t_obj["thm"]["value"], "proof_after": intro_e,
                            "meaning": f"{wanted['def']} := True and {wanted['thm']} := True.intro"}
        d_obj["def"]["value"] = true_e
        t_obj["thm"]["value"] = intro_e
        replace[d_offset] = dumps(d_obj)
        replace[t_offset] = dumps(t_obj)
    elif variant == "removed":
        offset, _, _ = decl_lines[("thm", wanted["thm"])]
        drop.add(offset)
        record["change"] = {"removed_theorem": wanted["thm"]}
    elif variant == "truncated":
        cut = first_main
        record["change"] = {"cut_at_byte": cut, "everything_from_the_first_main_theorem_on_is_missing": True}
    with open(options.source, "rb") as reader, open(options.target, "wb") as writer:
        offset = 0
        for raw in reader:
            if cut is not None and offset >= cut:
                break
            if offset in replace:
                writer.write(replace[offset])
            elif offset not in drop:
                writer.write(raw)
            offset += len(raw)
    record["bytes"] = os.path.getsize(options.target)
    record["seconds"] = round(time.monotonic() - started, 1)
    write_json(options.out, record)
    print(json.dumps(record), flush=True)
    return 0


# ---------------------------------------------------------------------------------------------
# Kernels and comparator
# ---------------------------------------------------------------------------------------------

def run(options) -> int:
    pins = load_pins()
    lower_priority()
    root = os.path.abspath(options.root)
    export = os.path.abspath(options.export)
    challenge = os.path.abspath(options.challenge) if options.challenge else ""
    folder = os.path.join(os.path.dirname(os.path.abspath(options.out)), "run-work")
    os.makedirs(folder, exist_ok=True)
    record = json.load(open(options.out, encoding="utf-8")) if os.path.isfile(options.out) else {}
    record.setdefault("label", options.label)
    record.setdefault("export", os.path.basename(export))
    record.setdefault("export_bytes", os.path.getsize(export))
    record.setdefault("steps", {})
    theorems = pins["GRH_THEOREMS"].split(",") if not options.theorems else options.theorems.split(",")
    axioms = pins["GRH_AXIOMS"].split(",")
    env = lean_env(root, None)
    cap = options.cap_min * 60
    began = time.monotonic()
    for step in [s for s in options.steps.split(",") if s]:
        if options.budget_min and time.monotonic() - began > options.budget_min * 60:
            record["steps"][step] = {"skipped": "budget"}
            continue
        print(f"--- {step} {time.strftime('%H:%M:%S')}", flush=True)
        if step.startswith("comparator"):
            config = comparator_config(os.path.join(folder, f"{step}.json"), theorems, axioms,
                                       challenge_module=pins["GRH_CHALLENGE_MODULE"],
                                       solution_module=pins["GRH_SOLUTION_MODULE"])
            cmd = comparator_command(root, config, challenge, export, sandbox=step == "comparator-bwrap" and IS_LINUX)
        elif step == "leche-compare":
            cmd = [options.leche_compare, "--compare", challenge, export]
        elif step == "ron-compare":
            cmd = [options.ron_compare, "--compare", challenge, export]
        else:
            cmd = kernel_command(root, step, export, folder)
        row = run_tree(cmd, env=env, cwd=folder, timeout=cap, memory_gib=options.memory_gib or None,
                       guard_gib=options.guard_gib or None, stack_unlimited=options.stack_unlimited)
        row["command"] = " ".join(os.path.basename(c) if os.path.isabs(c) else c for c in cmd)
        record["steps"][step] = row
        print(json.dumps({k: row.get(k) for k in ("exit", "ending", "wall_s", "tree_rss", "scope_anon", "killed")}),
              (row.get("says") or "")[-400:], flush=True)
        write_json(options.out, record)
    write_json(options.out, record)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("fetch")
    p.add_argument("--what", default="compact,readable,challenge")
    p.add_argument("--dest", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--keep-zst", action="store_true")
    p.add_argument("--trust-present", action="store_true", help="skip the download of a file already there (digest still checked)")
    p = sub.add_parser("precheck")
    p.add_argument("--tool", required=True)
    p.add_argument("--dest", required=True)
    p.add_argument("--what", default="compact,readable,challenge")
    p.add_argument("--out", required=True)
    p.add_argument("--cap-min", type=float, default=60)
    p.add_argument("--memory-gib", type=float, default=0)
    p = sub.add_parser("precheck-dir")
    p.add_argument("--tool", required=True)
    p.add_argument("--dir", required=True)
    p.add_argument("--theorems", required=True)
    p.add_argument("--out", required=True)
    p = sub.add_parser("doctor")
    p.add_argument("--source", required=True)
    p.add_argument("--variant", required=True, choices=sorted(DOCTOR_TARGETS))
    p.add_argument("--target", required=True)
    p.add_argument("--out", required=True)
    p = sub.add_parser("run")
    p.add_argument("--root", required=True)
    p.add_argument("--export", required=True)
    p.add_argument("--challenge", default="")
    p.add_argument("--steps", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--theorems", default="")
    p.add_argument("--memory-gib", type=float, default=0)
    p.add_argument("--guard-gib", type=float, default=0)
    p.add_argument("--cap-min", type=float, default=120)
    p.add_argument("--budget-min", type=float, default=0)
    p.add_argument("--leche-compare", default="")
    p.add_argument("--ron-compare", default="")
    p.add_argument("--stack-unlimited", action="store_true")
    p.add_argument("--label", default="")
    p = sub.add_parser("pin")
    p.add_argument("key")
    options = parser.parse_args()
    if options.command == "pin":
        print(load_pins()[options.key])
        return 0
    lower_priority()
    return {"fetch": fetch, "precheck": precheck, "precheck-dir": precheck_dir, "doctor": doctor,
            "run": run}[options.command](options)


if __name__ == "__main__":
    sys.exit(main())
