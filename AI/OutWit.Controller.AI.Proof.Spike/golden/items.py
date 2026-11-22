"""PF-0 spike: check items derived from dataset rows, and the made rejections derived from them.

The golden set is committed as a list (ids, hashes, licences) only. This module is the one
place that turns a dataset row into an item (a trusted challenge and an untrusted solution in
the form of the `check` mode) and that applies a made rule to an item; the selection and the
fetch both use it, so the fetch reproduces the selection's texts and proves it by the hashes.

A row becomes an item only when its statement is one theorem (after the imports, the `open`
lines and the comments), its proof is one declaration of the same name under the same `open`
lines, and the name is a plain identifier. Comments are blanked (the positions of the code do
not move).
"""
from __future__ import annotations

import hashlib
import re

LICENCE = "Apache-2.0"
SOURCE = "project-numina/NuminaMath-LEAN"

COMMAND_STARTS = {
    "import", "open", "set_option", "theorem", "lemma", "def", "abbrev", "instance", "example",
    "namespace", "section", "end", "variable", "universe", "local", "attribute", "private",
    "protected", "noncomputable", "structure", "inductive", "class", "macro", "syntax", "notation",
    "infix", "infixl", "infixr", "prefix", "postfix", "axiom", "opaque", "unsafe", "partial",
    "mutual", "scoped", "export", "elab", "macro_rules", "initialize", "omit", "include", "alias",
    "irreducible_def", "nonrec", "public", "meta", "module",
}
# Constructs the sanitizer of the `check` mode refuses in a solution; such rows are left out.
REFUSED = re.compile(
    r"(#eval|#exit|\brun_cmd\b|\brun_elab\b|\brun_meta\b|\belab\b|\belab_rules\b|\bmacro\b|\bmacro_rules\b"
    r"|\bsyntax\b|\binitialize\b|\bbuiltin_initialize\b|\baxiom\b|\bunsafe\b|@\[\s*extern|@\[\s*implemented_by"
    r"|\bset_option\b|\bimport\b)")
NAME = re.compile(r"^[A-Za-z_][A-Za-z0-9_']*$")
SORRY = re.compile(r"\bsorry\b")


def sha(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def blank_comments(text: str) -> str:
    """Replaces every comment by spaces (line ends kept); strings are left alone."""
    out = []
    i, n = 0, len(text)
    while i < n:
        ch = text[i]
        if ch == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("--", i):
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i))
            i = j
        elif text.startswith("/-", i):
            depth, j = 1, i + 2
            while j < n and depth > 0:
                if text.startswith("/-", j):
                    depth, j = depth + 1, j + 2
                elif text.startswith("-/", j):
                    depth, j = depth - 1, j + 2
                else:
                    j += 1
            out.append("".join(c if c == "\n" else " " for c in text[i:j]))
            i = j
        else:
            out.append(ch)
            i += 1
    return "".join(out)


def commands(text: str) -> list | None:
    """Top-level commands of a comment-free text: (first word, text). None when the text does
    not start with a command this splitter knows."""
    result = []
    current = None
    for raw in text.replace("\r\n", "\n").split("\n"):
        line = raw.rstrip()
        if not line:
            if current is not None:
                current[1].append("")
            continue
        starts = False
        if not line[0].isspace():
            word = re.match(r"[A-Za-z_]+|@\[|#", line)
            starts = bool(word) and (word.group(0) in COMMAND_STARTS or word.group(0) in ("@[", "#"))
        if starts:
            current = [re.match(r"[A-Za-z_#@\[]+", line).group(0), [line]]
            result.append(current)
        elif current is None:
            return None
        else:
            current[1].append(line)
    out = []
    for word, lines in result:
        while lines and not lines[-1]:
            lines.pop()
        out.append((word, "\n".join(lines)))
    return out


def _shape(text: str) -> tuple:
    """(opens, declarations, dropped budget lines) of a file's text, or a reason it is not usable."""
    parts = commands(blank_comments(text))
    if parts is None or not parts:
        return None, "unknown_start"
    opens, rest = [], []
    for word, body in parts:
        if word == "import":
            continue
        if word == "open":
            if "\n" in body or re.search(r"\bin$", body) or SORRY.search(body):
                return None, "open_in"
            opens.append(" ".join(body.split()))
        elif word == "set_option":
            if "\n" in body or re.search(r"\bin$", body) or "maxHeartbeats" not in body:
                return None, "set_option"
        else:
            rest.append((word, body))
    return (opens, rest), None


def derive(statement: str, proof: str) -> tuple:
    """(item fields, None) or (None, reason). The item: name, opens, head (the statement up to
    and including `:=`), challenge, solution."""
    shaped, reason = _shape(statement or "")
    if reason:
        return None, "statement_" + reason
    opens, rest = shaped
    if len(rest) != 1 or rest[0][0] not in ("theorem", "lemma"):
        return None, "statement_shape"
    text = rest[0][1].rstrip()
    match = re.match(r"(theorem|lemma)\s+(\S+)", text)
    if not match or not NAME.match(match.group(2)):
        return None, "statement_name"
    name = match.group(2)
    if text.endswith("sorry"):
        text = text[:-len("sorry")].rstrip()
    if text.endswith("by"):
        text = text[:-2].rstrip()
    if not text.endswith(":="):
        return None, "statement_end"
    head = text
    challenge = head + " by sorry"

    shaped, reason = _shape(proof or "")
    if reason:
        return None, "proof_" + reason
    proof_opens, rest = shaped
    if proof_opens != opens:
        return None, "opens_differ"
    if len(rest) != 1 or rest[0][0] not in ("theorem", "lemma"):
        return None, "proof_shape"
    solution = rest[0][1].rstrip()
    match = re.match(r"(theorem|lemma)\s+(\S+)", solution)
    if not match or match.group(2) != name:
        return None, "proof_name"
    if REFUSED.search(solution):
        return None, "refused_construct"
    return {"name": name, "opens": opens, "head": head, "challenge": challenge, "solution": solution}, None


# --------------------------------------------------------------------------------------------
# Made rejections: rules applied to an item of the set (and, for a mismatch, a second one)
# --------------------------------------------------------------------------------------------

MADE_AXIOM = "axiom pf0_made_axiom (p : Prop) : p"


def made(rule: str, base: dict, other: dict | None = None, param: str = "") -> dict | None:
    """The item a made rule gives, or None when the rule does not apply to this base."""
    item = {"name": base["name"], "opens": list(base["opens"]), "challenge": base["challenge"]}
    if rule == "sorry":
        # The statement itself, handed in as its own proof.
        item["solution"] = base["challenge"]
    elif rule == "mismatch":
        # Another item's proved declaration under this item's name: a valid proof of a different statement.
        if other is None or other["opens"] != base["opens"] or other["name"] == base["name"]:
            return None
        renamed, count = re.subn(r"^(theorem|lemma)\s+" + re.escape(other["name"]) + r"(?=\s)",
                                 "theorem " + base["name"], other["solution"], count=1)
        if count != 1 or re.search(r"\b" + re.escape(other["name"]) + r"\b", renamed):
            return None
        item["solution"] = renamed
    elif rule == "drop_last":
        # The proof without its last line.
        lines = base["solution"].split("\n")
        body = len(lines) - len(base["head"].split("\n"))
        if body < 2:
            return None
        item["solution"] = "\n".join(lines[:-1])
    elif rule == "axiom":
        # The statement "proved" by an added axiom.
        item["solution_pre"] = [MADE_AXIOM]
        item["solution"] = base["head"] + " pf0_made_axiom _"
    elif rule == "budget":
        # The same proof under a smaller or a just sufficient budget (`param`, in thousands of heartbeats).
        item["solution"] = base["solution"]
        item["max_heartbeats"] = int(param)
    elif rule == "memout":
        # The same proof with the process's memory cap far below what the environment needs.
        item["solution"] = base["solution"]
        item["memory_mb"] = int(param)
    else:
        raise ValueError(rule)
    return item


def authored() -> list:
    """Items written for the gate (not from the dataset): closed arithmetic facts proved by the
    kernel, by a native decision procedure, and by a brute-force computation that cannot end
    inside the hard cap."""
    out = []
    facts = [
        ("pow_mod", f"2 ^ 200 % 1000007 = {pow(2, 200, 1000007)}"),
        ("pow_mod2", f"3 ^ 300 % 998244353 = {pow(3, 300, 998244353)}"),
        ("sum", f"(List.range 2000).foldl (fun a b => a + b) 0 = {sum(range(2000))}"),
        ("factorial", f"Nat.factorial 25 % 1000003 = {_factorial(25) % 1000003}"),
        ("gcd", f"Nat.gcd (2 ^ 90 - 1) (2 ^ 60 - 1) = {_gcd(2 ** 90 - 1, 2 ** 60 - 1)}"),
    ]
    for key, statement in facts:
        for tactic in ("native_decide", "decide", "norm_num"):
            name = f"pf0_fact_{key}_{tactic}"
            head = f"theorem {name} : {statement} :="
            out.append({"id": f"made-fact-{key}-{tactic}", "rule": "fact:" + tactic, "name": name,
                        "opens": [], "challenge": head + " by sorry", "solution": head + f" by {tactic}"})
    name = "pf0_slow_search"
    head = f"theorem {name} : Nat.repeat (fun x => (x * 3 + 1) % 1000003) 30000000000 1 = 0 :="
    for index in range(2):
        out.append({"id": f"made-timeout-{index}", "rule": "timeout", "name": name, "opens": [],
                    "challenge": head + " by sorry", "solution": head + " by native_decide",
                    "hard_cap_s": 90 + 30 * index})
    return out


def _factorial(n: int) -> int:
    value = 1
    for k in range(2, n + 1):
        value *= k
    return value


def _gcd(a: int, b: int) -> int:
    while b:
        a, b = b, a % b
    return a
