# 01 - Known answers: 469 SMT-LIB problems with their verdicts

The slice of the SMT-LIB release 2025 that the controller's own tests solve: 469 problems of ten
logics (linear integer and real arithmetic, bit-vectors, uninterpreted functions, arrays,
datatypes, strings and their combinations), each with the verdict its authors gave it in its
`:status`, 225 `sat` and 244 `unsat`. The tests name the problems by a committed list of paths and
hashes and fetch them from the collection's archives; nothing of them is in the repository. Here
the same problems go through the fleet: every answer must be the list's, every `sat` model is
checked by the server's evaluator, and with every package on two machines every answer is compared.

## Files

- `make_set.py` fetches the slice with the tests' fetcher and list
  (`AI/OutWit.Controller.AI.Smt.Tests/Slice/fetch.py` and `fetch-list.tsv`) into a work folder and
  packs it with `smt-live pack`: one `check` row a problem, its id the list's (`p000` ...), its
  expected verdict the problem's `:status`; it prints the set's SHA-256 and holds it to the one
  `EXPECTED.md` names. The fetcher needs `zstd` on the PATH and streams the archives of the ten
  logics from Zenodo (an archive is a stream of up to 1.7 GB); a file already fetched is hashed,
  and fetched again unless it is the list's.
- `check.py` holds a run's documents to the kit: the set (its SHA-256), the pinned module and the
  options of the commands below; the list (every verdict, and no row of another problem); the
  statuses, the agreement with the expected verdicts and the copies `EXPECTED.md` gives; the
  evaluator's classes by logic that the solver's slice test pins (read from
  `Solver/SmtSolverSliceTests.cs`); and an integrity document that says, and says 0 for, the
  lies, the disagreements, the problems without a result and the standing disputes. It prints the
  table by logic and the run's totals.
- `tests/`: the tests of `make_set.py`, `check.py` and the slice fetcher, on made-up sets and runs
  (`python -I -m unittest discover -s tests -t tests`); the workflow "AI Smt" runs them.
- `EXPECTED.md`: what a run must show.

The work folder holds the fetched problems: it is never committed (`demo-out/` is ignored).

## Run

From `AI/OutWit.Controller.AI.Smt.Live`, with the environment of the README set:

```
dotnet build -c Release
python -I demo/01-known-answers/make_set.py demo-out/known
bin/Release/net10.0/smt-live preflight demo-out/known/problems.jsonl --rlimit 2000000000 --hard-cap 100 --rate 1
bin/Release/net10.0/smt-live run demo-out/known/problems.jsonl --rlimit 2000000000 --hard-cap 100 --rate 1 --out demo-out/known/run-1
python -I demo/01-known-answers/check.py demo-out/known/run-1
bin/Release/net10.0/smt-live run demo-out/known/problems.jsonl --rlimit 2000000000 --hard-cap 100 --rate 1 --out demo-out/known/run-2
bin/Release/net10.0/smt-live compare demo-out/known/run-1 demo-out/known/run-2
```

`--rlimit 2000000000 --hard-cap 100` is the budget of the solver's slice test, which none of the
469 problems comes near: under it every problem is decided. `--rate 1` sends every package to two
machines, so every answer is compared by its digest.

## What to look at

1. **The preflight**: 469 rows taken, none refused; the ten logics; 225 expected `sat` and 244
   `unsat`; the options as numbers; the upper bound of the work (469 problems times the hard cap)
   and the packages a run makes.
2. **The run**: the progress, then the four documents downloaded and each held to its schema.
3. **The summary**: every row `ok` but one, `p235`, which is `output_exceeded` (its model is more
   than the 16 MiB a session may write; its verdict stands); the verdicts `sat 225, unsat 244,
   unknown 0`; every expected
   verdict agreed with; the evaluator's classes by logic; every problem answered by two machines
   that agree; no lie, no disagreement between the evaluator and the solver, no problem without a
   result. `smt-live run` exits 3 and says why when any of that is not so.
4. **check.py** prints `held: everything`.
5. **compare**: the two runs give the same settled answers and the same provenance.
