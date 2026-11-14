"""Generates the golden position set: positions from seeded self-play of a Stockfish binary
(its own move generator and FEN output, so every position is legal and canonical), plus a few
hand-written terminal and endgame positions. The positions are ours (generated, MIT); no
third-party data. Categories cover openings, middlegames, endgames, mates, checkmates,
stalemates, en passant, castling, promotions and checks.

usage: python golden.py --engine <stockfish> --out <positions.jsonl> [--seed N] [--count 500]
The output is committed together with <out>.meta.json (seed, engine, counts, SHA-256), so a
later run does not need the generator.
"""

import argparse
import hashlib
import json
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sfuci import Engine  # noqa: E402

START = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"

# Hand-written positions (each checked by the engine: it normalises the FEN and would exit on
# an invalid one). Category, FEN.
HAND = [
    ("stalemate", "7k/5Q2/6K1/8/8/8/8/8 b - - 0 1"),
    ("stalemate", "k7/8/1Q6/8/8/8/8/7K b - - 0 1"),
    ("stalemate", "8/8/8/8/8/6k1/5q2/7K w - - 0 1"),
    ("stalemate", "5k2/5P2/5K2/8/8/8/8/8 b - - 0 1"),
    ("stalemate", "7k/7P/7K/8/8/8/8/8 b - - 0 1"),
    ("stalemate", "1R6/8/8/8/8/8/7R/k6K b - - 0 1"),
    ("checkmate", "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3"),
    ("checkmate", "r1bqkb1r/pppp1Qpp/2n2n2/4p3/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 0 4"),
    ("checkmate", "R5k1/5ppp/8/8/8/8/8/6K1 b - - 0 1"),
    ("checkmate", "6rk/5Npp/8/8/8/8/8/6K1 b - - 0 1"),
    ("checkmate", "k7/1Q6/1K6/8/8/8/8/8 b - - 0 1"),
    ("checkmate", "8/8/8/8/8/k7/r7/1r5K w - - 0 1"),
    ("endgame", "8/8/8/4k3/8/8/8/KQ6 w - - 0 1"),
    ("endgame", "8/8/8/4k3/8/8/8/KR6 w - - 0 1"),
    ("endgame", "8/8/8/4k3/8/8/8/KBN5 w - - 0 1"),
    ("endgame", "8/8/8/8/4k3/8/4P3/4K3 w - - 0 1"),
    ("endgame", "1K1k4/1P6/8/8/8/8/r7/2R5 w - - 0 1"),
    ("endgame", "8/8/8/3k4/8/8/3r4/KQ6 w - - 0 1"),
    ("promotion", "8/5k2/8/8/8/8/1p3K2/8 b - - 0 1"),
    ("promotion", "8/1P3k2/8/8/8/8/5K2/8 w - - 0 1"),
    ("draw-rule", "8/8/8/4k3/8/8/8/4KN2 w - - 0 1"),
    ("draw-rule", "8/8/8/4k3/8/8/8/KR6 w - - 99 120"),
]

# Category quotas, filled in this priority order from the self-play pool; the rest of the
# set is middlegames.
QUOTAS = [("terminal", 10), ("mate", 50), ("ep", 30), ("promotion", 28), ("castling", 45),
          ("check", 35), ("endgame", 75), ("opening", 80)]

CASTLING_MOVES = {"e1g1": "K", "e1c1": "K", "e8g8": "k", "e8c8": "k"}


def board_of(fen):
    board = {}
    ranks = fen.split()[0].split("/")
    for r, row in enumerate(ranks):
        f = 0
        for c in row:
            if c.isdigit():
                f += int(c)
            else:
                board["abcdefgh"[f] + str(8 - r)] = c
                f += 1
    return board


def piece_count(fen):
    return sum(1 for c in fen.split()[0] if c.isalpha())


def key_of(fen):
    return " ".join(fen.split()[:4])


def categories(entry):
    fen, legal, check, ply = entry["fen"], entry["legal"], entry["check"], entry["ply"]
    cats = set()
    if not legal:
        cats.add("terminal")
    if fen.split()[3] != "-":
        cats.add("ep")
    if any(len(m) == 5 for m in legal):
        cats.add("promotion")
    board = board_of(fen)
    if any(m in CASTLING_MOVES and board.get(m[:2]) == CASTLING_MOVES[m] for m in legal):
        cats.add("castling")
    if check and legal:
        cats.add("check")
    if piece_count(fen) <= 10:
        cats.add("endgame")
    if ply <= 14:
        cats.add("opening")
    return cats


def play_game(eng, rng, clean, nodes):
    fen, check = START, False
    out = []
    for ply in range(220):
        legal = eng.legal_moves(fen)
        out.append({"fen": fen, "ply": ply, "legal": legal, "check": check})
        if not legal or int(fen.split()[4]) >= 100 or piece_count(fen) <= 2:
            break
        p_random = 0.03 if clean else 0.2
        if (clean and ply < 8) or rng.random() < p_random:
            move = rng.choice(legal)
        else:
            move = eng.search(fen, [], nodes)["best"]
        fen, check = eng.fen_after(fen, [move])
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--engine", required=True)
    ap.add_argument("--engine-sha256", default="")
    ap.add_argument("--out", required=True)
    ap.add_argument("--seed", type=int, default=20261114)
    ap.add_argument("--count", type=int, default=500)
    ap.add_argument("--noisy-games", type=int, default=110)
    ap.add_argument("--clean-games", type=int, default=60)
    ap.add_argument("--move-nodes", type=int, default=400)
    ap.add_argument("--mate-nodes", type=int, default=10000)
    ap.add_argument("--low-priority", action="store_true")
    args = ap.parse_args()

    rng = random.Random(args.seed)
    eng = Engine(args.engine, hash_mb=1, low_priority=args.low_priority)

    pool = {}
    games = []
    for g in range(args.noisy_games + args.clean_games):
        game = play_game(eng, rng, g >= args.noisy_games, args.move_nodes)
        games.append(game)
        for e in game:
            pool.setdefault(key_of(e["fen"]), e)
    sys.stderr.write("games %d, unique positions %d\n" % (len(games), len(pool)))

    # Mate candidates: the last plies of games that ended in checkmate, plus a random sample.
    candidates = []
    for game in games:
        if not game[-1]["legal"] and game[-1]["check"]:
            candidates.extend(game[-12:-1])
    others = sorted(pool)
    candidates.extend(pool[k] for k in rng.sample(others, min(200, len(others))))
    mates = set()
    for e in candidates:
        k = key_of(e["fen"])
        if k in mates or not e["legal"]:
            continue
        r = eng.search(e["fen"], [], args.mate_nodes)
        if r["lines"] and "mate" in r["lines"][0]["score"]:
            mates.add(k)

    cats = {k: categories(e) for k, e in pool.items()}
    for k in mates:
        cats[k].add("mate")

    hand = []
    for cat, fen in HAND:
        norm, check = eng.fen_after(fen)
        hand.append({"fen": norm, "cat": cat, "src": "hand", "check": check,
                     "legal": eng.legal_moves(norm)})
    taken = {key_of(h["fen"]) for h in hand}

    chosen = list(hand)
    for cat, quota in QUOTAS:
        keys = sorted(k for k in pool if cat in cats[k] and k not in taken)
        for k in rng.sample(keys, min(quota, len(keys))):
            taken.add(k)
            chosen.append(dict(pool[k], cat=cat, src="selfplay"))
    rest = sorted(k for k in pool if k not in taken and pool[k]["legal"])
    need = args.count - len(chosen)
    for k in rng.sample(rest, need):
        chosen.append(dict(pool[k], cat="middlegame", src="selfplay"))
    eng.close()

    rng.shuffle(chosen)
    counts = {}
    with open(args.out, "w", encoding="ascii", newline="\n") as f:
        for i, e in enumerate(chosen):
            counts[e["cat"]] = counts.get(e["cat"], 0) + 1
            rec = {"id": "g-%04d" % (i + 1), "fen": e["fen"], "cat": e["cat"], "src": e["src"],
                   "legal": len(e["legal"]), "check": bool(e["check"])}
            f.write(json.dumps(rec, sort_keys=True, separators=(",", ":")) + "\n")
    with open(args.out, "rb") as f:
        digest = hashlib.sha256(f.read()).hexdigest()
    meta = {"generator": "golden.py", "seed": args.seed, "count": len(chosen),
            "engine": eng.id_name, "engine_sha256": args.engine_sha256,
            "move_nodes": args.move_nodes, "mate_nodes": args.mate_nodes,
            "noisy_games": args.noisy_games, "clean_games": args.clean_games,
            "unique_selfplay_positions": len(pool),
            "legal_moves_total": sum(len(e["legal"]) for e in chosen),
            "categories": dict(sorted(counts.items())), "sha256": digest}
    with open(args.out + ".meta.json", "w", encoding="ascii", newline="\n") as f:
        f.write(json.dumps(meta, indent=1, sort_keys=True) + "\n")
    print(json.dumps(meta, indent=1, sort_keys=True))


if __name__ == "__main__":
    main()
