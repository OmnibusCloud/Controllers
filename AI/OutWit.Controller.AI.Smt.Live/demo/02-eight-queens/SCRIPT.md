# 02 - Eight queens: the smoke test of a fleet

Two enumerations whose answers a person can check by hand, small enough to take seconds on any
fleet: the shortest way to see that the solver runs on the nodes, that its models come back and
are checked, and that copies on two machines agree.

- `eight-queens`: eight queens on a chessboard, one on each row (`q0` to `q7` are their columns),
  no two on a column or a diagonal. Projected on `q0..q7` the problem has 92 models; asked for up
  to 100, the enumeration finds all 92, and its last check says `unsat`: the count is exact.
- `latin-5x5`: a 5x5 Latin square (each of 1 to 5 once in every row and every column) with six
  cells given. Exactly one square completes them; asked for up to two, the enumeration finds one,
  and its second check says `unsat`.

## Files

- `problems.jsonl`: the two rows, `enumerate`, each with its projection, its number of models and
  `expected: sat`.
- `make_set.py` writes `problems.jsonl`; run again, it writes the same bytes.

## Run

From `AI/OutWit.Controller.AI.Smt.Live`, with the environment of the README set:

```
dotnet build -c Release
bin/Release/net10.0/smt-live preflight demo/02-eight-queens/problems.jsonl --rate 1
bin/Release/net10.0/smt-live run demo/02-eight-queens/problems.jsonl --rate 1 --out demo-out/queens
```

The default budget is far more than either problem needs. `--rate 1` sends the one package to two
machines of 8 GB or more (a copy a machine refuses goes out again, to two machines; a fleet of
one such machine answers it on that machine alone, and the run says the sampling was `short`).

## What to look at

- The summary: `verdicts: sat 2`, `expected: agree 2`, `validation: validated 2`: every one of the
  93 models was evaluated against its problem on the server and satisfies it.
- `enumerate: exactly one 1, two or more 1`, `kept 1`, `dropped 0`: the Latin square (row 1) has
  one solution, the queens (row 0) more.
- In `demo-out/queens/results.jsonl`: the queens' row has `count` 92, `complete` true,
  `limit_reached` false; the square's row has `count` 1, `complete` true, and its one model's
  values are the square of `EXPECTED.md`.
