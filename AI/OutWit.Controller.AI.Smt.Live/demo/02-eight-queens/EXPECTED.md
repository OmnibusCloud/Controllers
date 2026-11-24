# 02 - Eight queens: the expected result

What a run must show follows from the two problems alone; the tool's tests hold the committed
rows to it without a solver: the model package's reader takes both rows, every one of the 92
placements of eight queens satisfies the first problem by the model evaluator and no other
placement of a queen a row does, and the six given cells have exactly one completion, which
satisfies the second. What only a fleet measures is in the table at the end, from the first real
run on the test fleet.

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
| Copies (`--rate 1`, a fleet of two machines of 8 GB or more) | sampling `as_asked`; 2 problems answered by two machines, both agreeing |
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

The first real run of `SCRIPT.md` on the test fleet; everything of "The run" above held.

| | |
|---|---|
| Date, controller version | 2026-11-24, Smt 0.4.0 (scripts 0.2.0) |
| Nodes and their platforms | 3: Windows x64 2, macOS arm64 1 (each 15 GiB or more); three Linux x64 machines of 6 GB were online, below the controller's floor of 7,680 MiB, and took no work |
| Wall time (`completed in`) | 00:00:03 |
| `eight-queens`: digest, fuel, resource count, checks, `model_truncated` | `b744c8ada6aca2ba554c21658a65346a754afd4f4ee89caeec645992ef2c25a6`, 3,367,145,445, 3,512,328, 93, false |
| `latin-5x5`: digest, fuel, resource count, checks | `447e76c2b00fc1fa236a9f5c09ca8d87d4dcf2f92a3f5322c516cfec91817d86`, 77,689,501, 22,858, 2 |
