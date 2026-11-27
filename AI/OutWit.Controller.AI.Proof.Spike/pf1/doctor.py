"""PF-1 spike: a copy of an export with one definition changed (spec section 15, "a changed
definition the statement uses"): what a project that defines a constant otherwise than the trusted
environment does would hand in.

    doctor.py <export.ndjson> <out.ndjson> <constant> hints|value <body>

`hints` changes only the definition's reducibility hints (the kernel accepts the copy as it
accepts the original; only an identity check notices). `value` replaces the body under the
definition's leading binders by another term of the same type (`nat0`: the literal 0; `apply10`:
the second binder applied to the first, `f x`), so the copy means something else. The new terms
get fresh indices after the last one of the export and the changed definition moves to the end of
the file, so every index stays dense and in order.
"""
from __future__ import annotations

import json
import os
import sys
from array import array

MODES = {
    "R1": [{"label": "hints", "constant": "Nat.card", "how": "hints"},
           {"label": "value", "constant": "Nat.card", "how": "value", "body": "nat0"}],
    "R2": [{"label": "hints", "constant": "partialFourierSum", "how": "hints"},
           {"label": "value", "constant": "partialFourierSum", "how": "value", "body": "apply10"}],
    "tiny": [{"label": "hints", "constant": "myDouble", "how": "hints"},
             {"label": "value", "constant": "myDouble", "how": "value", "body": "nat0"}],
}


def dumps(obj: dict) -> str:
    return json.dumps(obj, separators=(",", ":"), ensure_ascii=False, sort_keys=True)


def doctor(source: str, target: str, mode: dict) -> dict:
    names: list = [None]
    expr_offsets = array("Q")
    def_line = None
    def_offset = None
    wanted = mode["constant"]
    with open(source, "rb") as handle:
        offset = 0
        for raw in handle:
            head = raw[:6]
            if head == b'{"in":':
                obj = json.loads(raw)
                if "str" in obj:
                    pre = obj["str"]["pre"]
                    part = obj["str"]["str"]
                else:
                    pre = obj["num"]["pre"]
                    part = str(obj["num"]["i"])
                prefix = names[pre] if pre < len(names) else None
                full = part if not prefix else f"{prefix}.{part}"
                while len(names) <= obj["in"]:
                    names.append(None)
                names[obj["in"]] = full
            elif b'"ie":' in raw:
                # An expression line; its keys are written in sorted order, so "ie" need not lead.
                expr_offsets.append(offset)
            elif raw.startswith(b'{"def":') and def_line is None:
                obj = json.loads(raw)
                if names[obj["def"]["name"]] == wanted:
                    def_line = obj
                    def_offset = offset
            offset += len(raw)
    if def_line is None:
        raise SystemExit(f"{wanted}: no definition of that name in {source}")
    record = {"constant": wanted, "how": mode["how"], "before": dict(def_line["def"])}
    new_exprs: list = []
    body = def_line["def"]
    if mode["how"] == "hints":
        hints = body["hints"]
        body["hints"] = {"regular": hints["regular"] + 1} if isinstance(hints, dict) else {"regular": 1}
    else:
        def line_of(idx: int) -> dict:
            with open(source, "rb") as reader:
                reader.seek(expr_offsets[idx])
                obj = json.loads(reader.readline())
            assert obj["ie"] == idx, (obj, idx)
            return obj

        # The leading binders of the value.
        binders = []
        node = line_of(body["value"])
        while "lam" in node:
            binders.append(node["lam"])
            node = line_of(node["lam"]["body"])
        fresh = len(expr_offsets)

        def add(obj: dict) -> int:
            nonlocal fresh
            obj = dict(obj)
            obj["ie"] = fresh
            new_exprs.append({"ie": fresh, **{k: v for k, v in obj.items() if k != "ie"}})
            fresh += 1
            return fresh - 1

        if mode["body"] == "nat0":
            inner = add({"natVal": "0"})
        elif mode["body"] == "apply10":
            if len(binders) < 2:
                raise SystemExit(f"{wanted}: fewer than two binders")
            inner = add({"app": {"fn": add({"bvar": 1}), "arg": add({"bvar": 0})}})
        else:
            raise SystemExit(mode["body"])
        for binder in reversed(binders):
            inner = add({"lam": {"name": binder["name"], "type": binder["type"], "body": inner,
                                 "binderInfo": binder["binderInfo"]}})
        body["value"] = inner
        record["binders"] = len(binders)
    record["after"] = dict(body)
    record["new_exprs"] = len(new_exprs)
    with open(source, "rb") as reader, open(target, "wb") as writer:
        offset = 0
        for raw in reader:
            if offset != def_offset:
                writer.write(raw)
            offset += len(raw)
        for obj in new_exprs:
            writer.write((dumps(obj) + "\n").encode("utf-8"))
        writer.write((dumps({"def": body}) + "\n").encode("utf-8"))
    record["bytes"] = os.path.getsize(target)
    return record


def main() -> int:
    source, target, constant, how = sys.argv[1:5]
    mode = {"label": how, "constant": constant, "how": how, "body": sys.argv[5] if len(sys.argv) > 5 else "nat0"}
    print(json.dumps(doctor(source, target, mode), ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
