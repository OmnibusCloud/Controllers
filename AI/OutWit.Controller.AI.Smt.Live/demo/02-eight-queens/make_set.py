#!/usr/bin/env python3
"""Writes problems.jsonl of the eight-queens kit: two enumerate rows, the smoke test of a fleet.

    python -I make_set.py

eight-queens: a queen on each row of a chessboard, q0 to q7 the column of the queen of each
row, no two on a column or a diagonal. Projected on q0..q7 it has 92 models; asked for up to
100, the enumeration finds all 92 and its last check says unsat: the count is exact.

latin-5x5: a 5x5 Latin square (each of 1..5 once in every row and every column) with six cells
given. Exactly one square completes them; asked for up to two models, the enumeration finds one
and its second check says unsat.

Run it again and the file does not change.
"""

import json
import os

QUEENS = 8
ORDER = 5

# The six given cells of the Latin square: (row, column) from 1, and the value.
GIVEN = {(2, 5): 4, (3, 3): 4, (3, 5): 5, (4, 2): 1, (4, 4): 3, (5, 2): 3}


def queens():
    names = ["q%d" % row for row in range(QUEENS)]
    lines = ["(set-logic QF_LIA)"]
    lines += ["(declare-const %s Int)" % name for name in names]
    lines += ["(assert (and (<= 0 %s) (< %s %d)))" % (name, name, QUEENS) for name in names]
    lines.append("(assert (distinct %s))" % " ".join(names))
    lines.append("(assert (distinct %s))" % " ".join("(+ %s %d)" % (name, row) for row, name in enumerate(names)))
    lines.append("(assert (distinct %s))" % " ".join("(- %s %d)" % (name, row) for row, name in enumerate(names)))
    return {
        "id": "eight-queens",
        "query": "enumerate",
        "smt2": "\n".join(lines) + "\n",
        "project": names,
        "max_models": 100,
        "expected": "sat",
        "tags": "92 models, complete",
    }


def latin():
    cells = [[("x%d%d" % (row, column)) for column in range(1, ORDER + 1)] for row in range(1, ORDER + 1)]
    names = [name for row in cells for name in row]
    lines = ["(set-logic QF_LIA)"]
    lines += ["(declare-const %s Int)" % name for name in names]
    lines += ["(assert (and (<= 1 %s) (<= %s %d)))" % (name, name, ORDER) for name in names]
    lines += ["(assert (distinct %s))" % " ".join(row) for row in cells]
    lines += ["(assert (distinct %s))" % " ".join(cells[row][column] for row in range(ORDER)) for column in range(ORDER)]
    lines += ["(assert (= x%d%d %d))" % (row, column, value) for (row, column), value in sorted(GIVEN.items())]
    return {
        "id": "latin-5x5",
        "query": "enumerate",
        "smt2": "\n".join(lines) + "\n",
        "project": names,
        "max_models": 2,
        "expected": "sat",
        "tags": "exactly one model",
    }


def main():
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "problems.jsonl")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        for row in (queens(), latin()):
            handle.write(json.dumps(row) + "\n")
    print("wrote", path)


if __name__ == "__main__":
    main()
