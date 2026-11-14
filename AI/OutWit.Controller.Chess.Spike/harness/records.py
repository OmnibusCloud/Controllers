"""The canonical record: the bytes two runs of the same evaluation must agree on.

It holds the search result and nothing that depends on the clock or the placement: no time,
nps, hashfull, tbhits, no node or batch. One JSON object per line, keys sorted, no spaces,
ASCII only. Scores are from the side to move of the input position.
"""

import json

PROFILE = "sf19"


def dumps(record):
    return json.dumps(record, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def _line(info):
    out = {"multipv": info.get("multipv", 1), "score": info["score"], "depth": info.get("depth")}
    if "bound" in info:
        out["bound"] = info["bound"]
    else:
        out["bound"] = "exact"
    for key in ("seldepth", "nodes", "wdl", "pv"):
        if key in info:
            out[key] = info[key]
    return out


def _terminal(result):
    """'checkmate' or 'stalemate' when the engine answered without a search (no legal move)."""
    if result["best"] is not None or not result["lines"]:
        return None
    score = result["lines"][0]["score"]
    return "checkmate" if score.get("mate") == 0 else "stalemate"


def analyse_record(pos, nodes, multipv, result):
    rec = {"id": pos["id"], "stm": pos["stm"], "mode": "analyse", "profile": PROFILE,
           "nodes_limit": nodes, "multipv": multipv, "best": result["best"],
           "ponder": result["ponder"], "lines": [_line(i) for i in result["lines"]]}
    terminal = _terminal(result)
    if terminal:
        rec["terminal"] = terminal
    return rec


def negate_score(score):
    """A successor's score, from the successor's side to move, as a value of the move for the
    parent's side to move. A successor mated in k (mate -k) is a mate in k+1 for the parent;
    a successor that mates in k (mate +k) means the parent is mated in k."""
    if "cp" in score:
        return {"cp": -score["cp"]}
    m = score["mate"]
    if m <= 0:
        return {"mate": -m + 1}
    return {"mate": -m}


def score_key(score):
    """Orders scores from the parent's point of view: mating sooner > mating later > any cp >
    being mated later > being mated sooner."""
    if "cp" in score:
        return (1, score["cp"])
    m = score["mate"]
    if m > 0:
        return (2, -m)
    return (0, -m)


_SWAP_BOUND = {"exact": "exact", "lowerbound": "upperbound", "upperbound": "lowerbound"}


def action_value_entry(move, result):
    terminal = _terminal(result)
    if terminal == "checkmate":
        return {"move": move, "score": {"mate": 1}, "terminal": "checkmate"}
    if terminal == "stalemate":
        return {"move": move, "score": {"cp": 0}, "wdl": [0, 1000, 0], "terminal": "stalemate"}
    info = result["lines"][0]
    entry = {"move": move, "score": negate_score(info["score"]),
             "bound": _SWAP_BOUND[info.get("bound", "exact")], "depth": info.get("depth")}
    for key in ("seldepth", "nodes"):
        if key in info:
            entry[key] = info[key]
    if "wdl" in info:
        w, d, l = info["wdl"]
        entry["wdl"] = [l, d, w]
    entry["pv"] = info.get("pv", [])
    return entry


def terminal_of(result):
    return _terminal(result)


def action_values_record(pos, nodes, legal, entries, terminal=None):
    rec = {"id": pos["id"], "stm": pos["stm"], "mode": "action_values", "profile": PROFILE,
           "nodes_limit": nodes, "legal": len(legal), "moves": entries}
    if not legal:
        rec["terminal"] = terminal
        rec["best"] = None
        return rec
    best = None
    for e in entries:  # entries are sorted by UCI string: the first of equal values wins
        if best is None or score_key(e["score"]) > score_key(best["score"]):
            best = e
    rec["best"] = best["move"]
    rec["score"] = best["score"]
    if "wdl" in best:
        rec["wdl"] = best["wdl"]
    rec["pv"] = [best["move"]] + best.get("pv", [])
    return rec
