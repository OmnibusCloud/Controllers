# 02 - Eight queens: the expected result

What a run must show follows from the two problems alone; the tool's tests hold the committed
rows to it without a solver: the model package's reader takes both rows, every one of the 92
placements of eight queens satisfies the first problem by the model evaluator and no other
placement of a queen a row does, and the six given cells have exactly one completion, which
satisfies the second. What only a fleet measures is **not measured yet: the table at the end is
filled in after the first real run on the test fleet.**

## The set

`problems.jsonl`: 2 rows, 3,342 bytes, SHA-256
`8fcb09369a7fc3a9ed2d59eac093769fdd52d0d66359de1cf83244dca707fd08` (the name the provenance gives
it; the file is held to LF line ends on every checkout).

## The preflight

Runnable; 2 rows, 2 taken, 0 refused; logics `QF_LIA 2`; queries `enumerate 2`; expected `sat 2`;
the options taken; a node needs 2752 MiB for one session; the upper bound 120 s of the reference
machine; 1 package of at most 16 problems.

## The run

| | Must be |
|---|---|
| Documents | the four, each holding to its schema |
| Statuses | ok 2 |
| Verdicts | sat 2, unsat 0, unknown 0 |
| Expected | agree 2 |
| The evaluator's classes | validated 2 (all 93 models), partial 0, failed 0 |
| Enumerate | exactly one 1, two or more 1, no model 0, undecided 0; kept 1, dropped 0 |
| Copies (`--rate 1`, a fleet of two machines or more) | sampling `as_asked`; 2 problems answered by two machines, both agreeing |
| Integrity | lies 0, evaluator/solver disagreements 0, no result 0, disputes 0 |
| `smt-live run` | exit 0, `result   : OK` |

The rows of `results.jsonl`:

| Row | `count` | `complete` | `limit_reached` | `validation` |
|---|---|---|---|---|
| 0 `eight-queens` | 92 | true | false | validated |
| 1 `latin-5x5` | 1 | true | false | validated |

The one model of `latin-5x5` gives the square (`x11` to `x55`, row by row):

```
2 4 1 5 3
1 5 3 2 4
3 2 4 1 5
4 1 5 3 2
5 3 2 4 1
```

## Measured on the test fleet

Not measured yet. To be filled in after the first real run of `SCRIPT.md` on the test fleet.

| | |
|---|---|
| Date, controller version | |
| Nodes and their platforms | |
| Wall time (`completed in`) | |
| `eight-queens`: digest, fuel, resource count, checks, `model_truncated` | |
| `latin-5x5`: digest, fuel, resource count, checks | |
