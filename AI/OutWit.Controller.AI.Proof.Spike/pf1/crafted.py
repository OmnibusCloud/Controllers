"""PF-1 spike: untrusted exports into every reader and kernel (spec sections 5.3 and 15).

    crafted.py base --root <lean root> --work <dir>          (writes <dir>/base.ndjson)
    crafted.py gen  --base <base.ndjson> --dir <dir>
    crafted.py run  --root <lean root> --dir <dir> --out <record.json> [--driver <exe>]
                    [--programs a,b] [--cap-s 60] [--memory-gib 4] [--sandbox]

`base` elaborates a small file of core Lean and exports it (the control every program accepts).
`gen` writes the crafted inputs next to it: each is the base with one defect, so that every
program meets the same well-formed remainder. `run` feeds each input to each program with a wall
cap and a memory cap and records how it ended: an exit code, a signal, a kill at a cap. The record
names what was found, as findings; the inputs themselves stay on the runner.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

from common import (IS_LINUX, IS_WINDOWS, capped, comparator_command, comparator_config, export_targets,
                    kernel_command, lean_env, run_measured, tool, write_json)

BASE_LEAN = """
noncomputable def pf1Rec (n : Nat) : Nat := Nat.rec (motive := fun _ => Nat) 0 (fun _ ih => ih) n
theorem pf1_true : 2 + 2 = 4 := rfl
theorem pf1_other : 3 + 3 = 6 := rfl
theorem pf1_slow : pf1Rec 377 = 0 := rfl
def pf1Str : String := "pf1"
"""
BASE_THEOREMS = ["pf1_true", "pf1_other", "pf1_slow"]
PROGRAMS = ["leanchecker", "leanchecker-paranoid", "nanoda-4", "con-ron", "con-leche", "lean4lean",
            "comparator", "comparator-nanoda", "driver"]


def base(options: argparse.Namespace) -> int:
    root = os.path.abspath(options.root)
    work = os.path.abspath(options.work)
    os.makedirs(work, exist_ok=True)
    with open(os.path.join(work, "Base.lean"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(BASE_LEAN)
    env = lean_env(root, [work])
    built = run_measured([tool(root, "lean"), "-o", "Base.olean", "Base.lean"], env=env, cwd=work, timeout=600)
    if built["exit"] != 0:
        print(built["stdout"], built["stderr"])
        return 1
    target = os.path.join(work, "base.ndjson")
    names = export_targets(BASE_THEOREMS + ["pf1Str"])
    out = run_measured([tool(root, "leanexport"), "Base", "--"] + names, env=env, cwd=work, timeout=600,
                       stdout_path=target)
    print(f"base export: exit {out['exit']}, {os.path.getsize(target)} bytes")
    return out["exit"]


class Builder:
    """The base export, read enough to append to it: names and expressions by index."""

    def __init__(self, path: str):
        with open(path, encoding="utf-8") as handle:
            self.lines = handle.read().splitlines()
        self.names = {0: ""}
        self.name_count = 1
        self.expr_count = 0
        self.level_count = 1
        self.const_expr = {}
        self.decl_lines = {}
        self.nat_lits = {}
        for index, line in enumerate(self.lines[1:], start=1):
            obj = json.loads(line)
            if "in" in obj:
                part = obj.get("str") or obj.get("num")
                pre = self.names.get(part["pre"], "")
                piece = part["str"] if "str" in part else str(part["i"])
                self.names[obj["in"]] = f"{pre}.{piece}" if pre else piece
                self.name_count = max(self.name_count, obj["in"] + 1)
            elif "il" in obj:
                self.level_count = max(self.level_count, obj["il"] + 1)
            elif "ie" in obj:
                self.expr_count = max(self.expr_count, obj["ie"] + 1)
                if "const" in obj and obj["const"]["us"] == []:
                    self.const_expr[self.names[obj["const"]["name"]]] = obj["ie"]
                if "natVal" in obj:
                    self.nat_lits[obj["natVal"]] = (index, obj["ie"])
            else:
                kind = next(iter(obj))
                if kind in ("def", "thm", "axiom", "opaque"):
                    self.decl_lines[self.names[obj[kind]["name"]]] = index
        self.extra: list = []
        self.seen: dict = {}
        for line in self.lines[1:]:
            obj = json.loads(line)
            for key in ("ie", "il", "in"):
                if key in obj:
                    body = {k: v for k, v in obj.items() if k != key}
                    self.seen[(key, json.dumps(body, sort_keys=True))] = obj[key]

    def _add(self, key: str, body: dict, counter: str, canonical: bool = True) -> int:
        token = (key, json.dumps(body, sort_keys=True))
        if canonical and token in self.seen:
            return self.seen[token]
        idx = getattr(self, counter)
        setattr(self, counter, idx + 1)
        self.extra.append({key: idx, **body})
        self.seen[token] = idx
        return idx

    def name(self, text: str) -> int:
        return self._add("in", {"str": {"pre": 0, "str": text}}, "name_count")

    def expr(self, body: dict, canonical: bool = True) -> int:
        return self._add("ie", body, "expr_count", canonical)

    def level(self, body: dict) -> int:
        return self._add("il", body, "level_count")

    def definition(self, name: str, type_idx: int, value_idx: int) -> int:
        n = self.name(name)
        self.extra.append({"def": {"name": n, "levelParams": [], "type": type_idx, "value": value_idx,
                                   "hints": {"regular": 1}, "safety": "safe", "all": [n]}})
        return n

    def theorem(self, name: str, type_idx: int, value_idx: int) -> None:
        n = self.name(name)
        self.extra.append({"thm": {"name": n, "levelParams": [], "type": type_idx, "value": value_idx, "all": [n]}})

    def write(self, path: str, lines: list | None = None) -> None:
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            for line in (self.lines if lines is None else lines):
                handle.write(line + "\n")
            for obj in self.extra:
                handle.write(json.dumps(obj, separators=(",", ":"), sort_keys=True) + "\n")
        self.extra = []


def gen(options: argparse.Namespace) -> int:
    folder = os.path.abspath(options.dir)
    os.makedirs(folder, exist_ok=True)
    source = os.path.abspath(options.base)
    made = {}

    def fresh() -> Builder:
        return Builder(source)

    def put(label: str, b: Builder, lines: list | None = None, what: str = "") -> None:
        path = os.path.join(folder, f"{label}.ndjson")
        b.write(path, lines)
        made[label] = {"bytes": os.path.getsize(path), "what": what}

    b = fresh()
    put("ok", b, what="the base, unchanged (control)")

    b = fresh()
    cut = list(b.lines)
    cut[-1] = cut[-1][:len(cut[-1]) // 2]
    put("truncated-midline", b, cut, "the last line cut in half")

    b = fresh()
    last_thm = b.decl_lines["pf1_true"]
    put("truncated-decl", b, b.lines[:last_thm], "everything from a main theorem's line on is missing")

    b = fresh()
    lines = list(b.lines)
    lines.insert(len(lines) // 2, '{"in":')
    put("malformed-json", b, lines, "a line that is not JSON in the middle")

    b = fresh()
    b.extra.append({"widget": {"name": 1}})
    put("unknown-kind", b, what="a line of a kind the format does not have")

    b = fresh()
    b.extra.append({"in": 1, "str": {"pre": 0, "str": "again"}})
    put("index-reused", b, what="a name index bound a second time")

    b = fresh()
    n = b.expr_count
    b.extra.append({"ie": n, "app": {"fn": n + 1, "arg": 0}})
    b.extra.append({"ie": n + 1, "app": {"fn": n, "arg": 0}})
    b.expr_count += 2
    b.definition("pf1_cycle", b.const_expr["Nat"], n)
    put("cycle", b, what="two expressions that refer to each other")

    b = fresh()
    n = b.expr_count
    b.extra.append({"ie": n, "app": {"fn": n, "arg": n}})
    b.expr_count += 1
    b.definition("pf1_self", b.const_expr["Nat"], n)
    put("self-reference", b, what="an expression that refers to itself")

    b = fresh()
    lines = list(b.lines)
    lines.append(lines[b.decl_lines["pf1_true"]])
    put("duplicate-decl", b, lines, "a declaration line twice")

    b = fresh()
    put("meta-missing", b, b.lines[1:], "no meta line: the first line is a name")

    for version in ("3.2.0", "9.0.0"):
        b = fresh()
        lines = list(b.lines)
        meta = json.loads(lines[0])
        meta["meta"]["format"]["version"] = version
        lines[0] = json.dumps(meta, separators=(",", ":"), sort_keys=True)
        put(f"meta-{version}", b, lines, f"the meta line says format {version}")

    for digits in (100_000, 1_000_000, 10_000_000):
        b = fresh()
        lit = b.expr({"natVal": "7" * digits})
        b.definition("pf1_big", b.const_expr["Nat"], lit)
        put(f"literal-{digits}", b, what=f"a number literal of {digits} digits")

    for exponent in ("1e100000", "1e10000000000"):
        b = fresh()
        lines = list(b.lines)
        obj = json.loads(lines[1])
        text = json.dumps(obj, separators=(",", ":"), sort_keys=True)
        lines[1] = text[:-1] + f',"zz":{exponent}}}'
        put(f"json-number-{exponent}", b, lines, f"a JSON number written {exponent} on one line")

    b = fresh()
    lines = list(b.lines)
    depth = 200_000
    lines.insert(2, '{"zz":' + "[" * depth + "]" * depth + "}")
    put("json-nesting", b, lines, f"a line of {depth} nested JSON arrays")

    for depth in (100_000, 1_000_000):
        b = fresh()
        succ, zero = b.const_expr["Nat.succ"], b.const_expr["Nat.zero"]
        cur = zero
        for _ in range(depth):
            cur = b.expr({"app": {"fn": succ, "arg": cur}})
        b.definition("pf1_deep", b.const_expr["Nat"], cur)
        put(f"deep-term-{depth}", b, what=f"a term nested {depth} deep (Nat.succ applied {depth} times)")

    b = fresh()
    cur = 0
    below = 0
    depth = 100_000
    for _ in range(depth):
        below = cur
        cur = b.level({"succ": cur})
    sort_hi = b.expr({"sort": cur})
    sort_lo = b.expr({"sort": below})
    b.definition("pf1_level", sort_hi, sort_lo)
    put("deep-level", b, what=f"a universe level nested {depth} deep")

    # A term whose tree is exponentially larger than its graph: b(k+1) = Nat.add b(k) b(k).
    for label, twice, wrong in (("shared-bomb", False, False), ("shared-bomb-error", False, True),
                                ("duplicate-entries", True, False)):
        b = fresh()
        add, zero = b.const_expr["Nat.add"], b.const_expr["Nat.zero"]

        def bomb(levels: int, canonical: bool = True) -> int:
            cur = zero
            for _ in range(levels):
                cur = b.expr({"app": {"fn": b.expr({"app": {"fn": add, "arg": cur}}, canonical), "arg": cur}},
                             canonical)
            return cur

        first = bomb(200)
        if wrong:
            # Of type Nat, declared a String: the kernel's error message holds the term.
            b.definition("pf1_bomb", b.const_expr["String"], first)
        else:
            first_name = b.definition("pf1_bomb", b.const_expr["Nat"], first)
            second = bomb(200, canonical=not twice)
            second_name = b.definition("pf1_bomb2", b.const_expr["Nat"], second)
        if twice:
            # pf1_bomb = pf1_bomb2 by Eq.refl: the kernel must see two separately built copies agree.
            one = next(json.loads(line)["il"] for line in b.lines if line.startswith('{"il":') and line.endswith('"succ":0}'))
            eq_name = next(k for k, v in b.names.items() if v == "Eq")
            refl_name = next(k for k, v in b.names.items() if v == "Eq.refl")
            nat = b.const_expr["Nat"]
            eq_c = b.expr({"const": {"name": eq_name, "us": [one]}})
            refl_c = b.expr({"const": {"name": refl_name, "us": [one]}})
            c1 = b.expr({"const": {"name": first_name, "us": []}})
            c2 = b.expr({"const": {"name": second_name, "us": []}})
            ty = b.expr({"app": {"fn": b.expr({"app": {"fn": b.expr({"app": {"fn": eq_c, "arg": nat}}), "arg": c1}}),
                                 "arg": c2}})
            val = b.expr({"app": {"fn": b.expr({"app": {"fn": refl_c, "arg": nat}}), "arg": c1}})
            b.theorem("pf1_bomb_eq", ty, val)
        put(label, b, what="a term of 2^200 nodes as a tree, 400 as a graph" +
            (", declared of the wrong type" if wrong else
             ", built twice: the second copy repeats entries the export already has" if twice else
             ", and a theorem that it equals itself under another name"))

    b = fresh()
    index, idx = b.nat_lits["377"]
    lines = list(b.lines)
    obj = json.loads(lines[index])
    obj["natVal"] = "1000000000"
    lines[index] = json.dumps(obj, separators=(",", ":"), sort_keys=True)
    put("unary-recursion", b, lines, "a theorem the kernel can only check by a billion nested unfoldings")

    b = fresh()
    lines = list(b.lines)
    true_line = json.loads(lines[b.decl_lines["pf1_true"]])
    other_line = json.loads(lines[b.decl_lines["pf1_other"]])
    other_line["thm"]["value"] = true_line["thm"]["value"]
    lines[b.decl_lines["pf1_other"]] = json.dumps(other_line, separators=(",", ":"), sort_keys=True)
    put("false-theorem", b, lines, "a theorem with another theorem's proof (3 + 3 = 6 by the proof of 2 + 2 = 4)")

    for size, label in ((64 << 20, "long-string"), (1 << 30, "long-string-1g")):
        b = fresh()
        s = b.expr({"strVal": "x" * size})
        b.definition("pf1_long", b.const_expr["String"], s)
        put(label, b, what=f"a string literal of {size >> 20} MiB on one line")

    write_json(os.path.join(folder, "crafted.json"), made)
    for label, entry in made.items():
        print(f"{label}: {entry['bytes']} bytes - {entry['what']}")
    return 0


def run(options: argparse.Namespace) -> int:
    root = os.path.abspath(options.root)
    folder = os.path.abspath(options.dir)
    made = json.load(open(os.path.join(folder, "crafted.json"), encoding="utf-8"))
    programs = [p for p in options.programs.split(",") if p] if options.programs else PROGRAMS
    work = os.path.join(folder, "run")
    os.makedirs(work, exist_ok=True)
    env = lean_env(root, [])
    config = comparator_config(os.path.join(work, "comparator.json"), ["pf1_true"])
    nanoda_config = comparator_config(os.path.join(work, "comparator-nanoda.json"), ["pf1_true"], nanoda=True)
    challenge = os.path.join(folder, "ok.ndjson")
    record = {"programs": programs, "cap_s": options.cap_s, "memory_gib": options.memory_gib, "results": {}}
    labels = [label for label in made if not options.only or label in options.only.split(",")]
    for label in labels:
        path = os.path.join(folder, f"{label}.ndjson")
        rows = {}
        for program in programs:
            if program in ("comparator", "comparator-nanoda"):
                which = config if program == "comparator" else nanoda_config
                cmd = comparator_command(root, which, challenge, path, sandbox=options.sandbox and IS_LINUX)
            elif program == "driver":
                if not options.driver:
                    continue
                cmd = [options.driver, "slice", path, "--slices", "1", "--index", "0"]
            else:
                cmd = kernel_command(root, program, path, work)
            cap = options.cap_s * (2 if label == "unary-recursion" else 1)
            row = capped(cmd, env=env, cwd=work, timeout=cap, memory_gib=options.memory_gib)
            row.pop("json", None)
            row["ended"] = ending(row)
            rows[program] = row
            print(f"{label:24} {program:22} {row['ended']:28} {row['wall_s']:8.1f}s {row['peak_rss'] / 2**20:8.0f} MiB  "
                  f"{(row['says'] or '')[-140:]}", flush=True)
        record["results"][label] = {"what": made[label]["what"], "bytes": made[label]["bytes"], "runs": rows}
        write_json(options.out, record)
    return 0


def ending(row: dict) -> str:
    """How a run ended, in words: accepted, refused (an exit code), a cap, or a crash (a signal)."""
    if row.get("killed") == "timeout":
        return "cap: time"
    if row.get("killed") == "memory":
        return "cap: memory"
    code = row.get("exit")
    if code is None:
        return "did not start"
    if code == 0:
        return "accepted"
    if not IS_WINDOWS and code < 0:
        return f"crash: signal {-code}"
    if IS_WINDOWS and code >= 0xC0000000:
        return f"crash: 0x{code:08X}"
    if IS_LINUX and row.get("cap") == "cgroup" and code in (137, -9):
        return "cap: memory (cgroup)"
    if code in (134, 139) or code >= 128 and not IS_WINDOWS:
        return f"crash: exit {code}"
    return f"refused: exit {code}"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("step", choices=["base", "gen", "run"])
    parser.add_argument("--root", default="")
    parser.add_argument("--work", default="")
    parser.add_argument("--base", default="")
    parser.add_argument("--dir", default="")
    parser.add_argument("--out", default="")
    parser.add_argument("--driver", default="")
    parser.add_argument("--programs", default="")
    parser.add_argument("--only", default="")
    parser.add_argument("--cap-s", type=float, default=60)
    parser.add_argument("--memory-gib", type=float, default=4)
    parser.add_argument("--sandbox", action="store_true")
    options = parser.parse_args()
    if options.step == "base":
        return base(options)
    if options.step == "gen":
        return gen(options)
    return run(options)


if __name__ == "__main__":
    sys.exit(main())
