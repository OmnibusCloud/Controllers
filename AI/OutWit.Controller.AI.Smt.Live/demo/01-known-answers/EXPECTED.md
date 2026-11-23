# 01 - Known answers: the expected result

Two kinds of numbers are below. The ones a run must show come from the problems, the solver and
the evaluator alone: the committed list (`AI/OutWit.Controller.AI.Smt.Tests/Slice/fetch-list.tsv`)
and the controller's slice tests, which solve every problem of it in the controller's sandbox on
the build server (`Solver/SmtSolverSliceTests.cs`, `Slice/SmtSlicePreflightTests.cs`). They are the
same on any fleet, and `check.py` holds a run to them. The ones only a fleet measures (the wall
time, how the work was placed) are **not measured yet: the table at the end is filled in after the
first real run on the test fleet.**

The commands are those of `SCRIPT.md`: `--rlimit 2000000000 --hard-cap 100 --rate 1`.

## The set

`make_set.py` writes 469 rows, 22,349,801 bytes, SHA-256
`6772a20dc6dd6a19f32adeada38f0d15b9e3c92297e765b8eeeafef558150ac7` (measured with this tool's
`pack` on the 469 files the list names, each held to its SHA-256): the name the provenance gives
the set. `smt-live pack` reads it back as the server reads it:

```
checked  : 469 row(s) read as the server reads them: 469 taken, 0 refused
logics   : QF_LIA 150, QF_BV 150, QF_LRA 40, QF_UF 26, QF_AX 20, QF_DT 25, QF_ALIA 15, QF_UFLIA 20, QF_S 20, QF_UFDT 3
queries  : check 469
expected : sat 225, unsat 244 (from the problems' :status)
stripped : check-sat 469, exit 469, get-value 6 (left out by the sanitizer: the query decides what runs)
```

## The preflight

- runnable; 469 rows, 469 taken, 0 refused; the logics, queries, expected verdicts and stripped
  commands as above; the options taken;
- the budget: rlimit 2000000000, hard cap 100 s, fuel 500000000000, memory 1024 MB;
- a node needs 2752 MiB available for one session;
- the upper bound: 46900 s of the reference machine (469 problems times the hard cap);
- 30 packages of at most 16 problems (the planner's rule for a run of fewer than 16,384 problems).

## The run

| | Must be |
|---|---|
| Documents | the four, each holding to its schema |
| Statuses | ok 469; refused, input_error, output_exceeded, engine_crash, no_result, disputed 0 |
| Verdicts | sat 225, unsat 244, unknown 0: every problem's verdict the list's |
| Expected | agree 469, disagree 0, n/a 0 |
| The evaluator's classes | validated 217, partial 8, failed 0 |
| Copies (`--rate 1`, a fleet of two machines of 8 GB or more) | sampling `as_asked`; 469 problems answered by two machines, all of them agreeing; settled by `copies` 469; none rests on one machine |
| Integrity | lies 0, evaluator/solver disagreements 0, no result 0, disputes 0 |
| `smt-live run` | exit 0, `result   : OK` |
| `check.py` | `held: everything` |
| `compare` of two runs | `result   : the same settled answers and the same provenance` |

The evaluator's classes of the 225 satisfiable problems' models by logic, as the slice test of the
solver pins them (validated / partial / failed):

| Logic | Problems | sat | unsat | Classes |
|---|---|---|---|---|
| QF_LIA | 150 | 75 | 75 | 75 / 0 / 0 |
| QF_BV | 150 | 75 | 75 | 74 / 1 / 0 |
| QF_LRA | 40 | 20 | 20 | 20 / 0 / 0 |
| QF_UF | 26 | 10 | 16 | 10 / 0 / 0 |
| QF_AX | 20 | 10 | 10 | 10 / 0 / 0 |
| QF_DT | 25 | 6 | 19 | 4 / 2 / 0 |
| QF_ALIA | 15 | 6 | 9 | 6 / 0 / 0 |
| QF_UFLIA | 20 | 10 | 10 | 10 / 0 / 0 |
| QF_S | 20 | 10 | 10 | 5 / 5 / 0 |
| QF_UFDT | 3 | 3 | 0 | 3 / 0 / 0 |

A partial model is one the evaluator could not decide to the end (a construct it does not
compute; the report's `partial by reason` line names each reason); it accuses nobody and rests on
the copies.

The stats of a row (the resource count, the fuel, the checks) and its digest are functions of the
problem, the options and the module: they are the same on every machine and in every run, which
is what `compare` holds two runs to.

## Measured on the test fleet

Not measured yet. To be filled in after the first real run of `SCRIPT.md` on the test fleet.

| | Run 1 | Run 2 |
|---|---|---|
| Date, controller version | | |
| Nodes and their platforms | | |
| Wall time (`completed in`) | | |
| Fan-outs (main / repair / dispute): packages, complete, unanswered | | |
| Fuel in all, the most of one problem (`check.py`) | | |
| `compare` | | |
