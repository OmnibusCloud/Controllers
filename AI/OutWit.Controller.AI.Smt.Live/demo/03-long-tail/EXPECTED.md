# 03 - The long tail: the expected result

Two kinds of numbers are below. The ones a run must show come from the list, the problems, the
solver and the evaluator alone: they are the same on any fleet, and `check.py` holds a run to them.
The ones only a fleet measures (the wall time, how many machines took part, how the work was
placed, which problems a budget stopped there) go into the table at the end, from the first real
run on the test fleet.

The commands are those of `SCRIPT.md`: `--rlimit 4294967295 --hard-cap 300 --memory 1024 --rate 0.05`.

## The list

`make_list.py` writes `kit-list.tsv` from the SMT-LIB Catalog 2025 and the release's archives:
1,960 rows, SHA-256 `63cb03a1f5f76a60fe67430c9e5b90b3a6b5de528d61fdf2df8d26e6b5d211b4` (written
twice, from the archives and from the tables of a first run, with the same bytes). Every candidate
of the catalog was found in the release as the catalog describes it: QF_LIA 1,954 of 1,954, QF_BV
4,151 of 4,151.

| Logic | Problems | sat | unsat | Families | Bytes |
|---|---|---|---|---|---|
| QF_LIA | 811 | 509 | 302 | 21 | 94,465,017 |
| QF_BV | 1,149 | 559 | 590 | 26 | 159,931,092 |

Z3's time at the competition, by band (seconds of CPU; the sum over the kit is 22,500 s, the
median 7.1 s for QF_LIA and 5.1 s for QF_BV):

| Band | QF_LIA | QF_BV | Both |
|---|---|---|---|
| 1-2 s | 200 | 309 | 509 |
| 2-5 s | 114 | 259 | 373 |
| 5-10 s | 200 | 226 | 426 |
| 10-20 s | 75 | 152 | 227 |
| 20-40 s | 169 | 122 | 291 |
| 40-60 s | 53 | 81 | 134 |

The competition each time comes from (Z3's newest run on the problem): QF_LIA 2018 63, 2019 61,
2020 71, 2021 202, 2022 414; QF_BV 2018 75, 2019 87, 2020 96, 2021 304, 2022 587.

The families, the most first: QF_LIA `nec-smt` 200, `convert` 134, `20180326-Bromberger` 132,
`bofill-scheduling` 78, `CAV_2009_benchmarks` 48, `cut_lemmas` 36, `rings_preprocessed` 34,
`dillig` 23, `20220307-SMPT` 20, `rings` 20, `tropical-matrix` 17, `tightrhombus` 16, `slacks` 14,
`arctic-matrix` 13, `mathsat` 10, `RWS` 7, `CIRC` 4, `pidgeons` 2, `calypto`, `fft`, `wisa` 1 each;
QF_BV `Sage2`, `bruttomesso`, `sage`, `spear` 200 each, `20210219-Sydr` 96, `asp` 68, `float` 50,
`mcm` 26, `brummayerbiere3` 19, `20190311-bv-term-small-rw-Noetzli` 18, `brummayerbiere2` 15,
`brummayerbiere` 11, `log-slicing` 9, `bmc-bv-svcomp14` 7, `20210312-Bouvier` 6, `RWS` 6, `fft` 5,
`bmc-bv` 3, `rubik` 3, and one each of `2019-Mann`, `VS3`, `calypto`, `gulwani-pldi08`, `tacas07`,
`uum`, `wienand-cav2008`.

Every problem is under CC BY 4.0 (the catalog's licence of each; `make_set.py` writes the
attribution list from each file's own `:license` and `:source`). Sizes: from 724 bytes to 523,932,
the median 67,470.

## The set

`make_set.py` writes 1,960 rows, 257,872,798 bytes, SHA-256
`601ba8b9c880d8a5b0c3fcc2dad927e62a2344232dd109a6c1b029e3b167b265` (this tool's `pack` on the
1,960 files the list names, each held to its SHA-256): the name the provenance gives the set.
`smt-live pack` reads it back as the server reads it:

```
checked  : 1960 row(s) read as the server reads them: 1960 taken, 0 refused
logics   : QF_BV 1149, QF_LIA 811
queries  : check 1960
expected : unsat 892, sat 1068 (from the problems' :status)
stripped : check-sat 1960, exit 1960 (left out by the sanitizer: the query decides what runs)
```

`ATTRIBUTION.tsv`: every file states CC BY 4.0 or no licence (the collection's CC BY 4.0); 1,848
name their source in `:source`, the 112 that do not are named by their path.

## The preflight

- runnable; 1,960 rows, 1,960 taken, 0 refused; the logics, queries, expected verdicts and stripped
  commands as above; the options taken;
- the budget: rlimit 4294967295, hard cap 300 s, fuel 1500000000000, memory 1024 MB;
- a node needs 2752 MiB available for one session;
- the upper bound: 588000 s of the reference machine (1,960 problems times the hard cap);
- 123 packages of at most 16 problems (the planner's rule for a run of fewer than 16,384 problems).

## The run

| | Must be |
|---|---|
| Documents | the four, each holding to its schema |
| Statuses | every row `ok` or `output_exceeded`; refused, input_error, engine_crash, no_result, disputed 0 |
| Verdicts | each row's verdict its problem's `:status`, or `unknown` with the reason `rlimit`, `fuel` or `memout` (a budget's end); never the opposite verdict |
| Expected | disagree 0 |
| The evaluator's classes | failed 0; validated at least 95 % of the `sat` rows |
| Integrity | lies 0, evaluator/solver disagreements 0, no result 0, standing disputes 0 |
| `check.py` | `held: everything` |
| `compare` of two runs | `result   : the same settled answers and the same provenance` |

How many problems a budget stops is not written here as a number a run must show: the kit has not
been run whole yet (the build server solves its sample, below). Fuel is a count, so whatever the
first run shows, every later run of the same set under the same options must show again, on any
fleet: the same problems decided, the same ones stopped, the same fuel each; `compare` holds two
runs to that.

## The sample on the build server

`SmtLongTailSampleTests` (workflow "AI Smt long tail") solves the forty problems the list marks
as the sample, under the options above, four sessions at once, each file held to the list's
SHA-256:

| | Must be |
|---|---|
| Problems | 40: QF_LIA and QF_BV, 10 `sat` and 10 `unsat` each, at evenly spaced places of their competition times |
| Verdicts | every one the problem's `:status`; none undecided |
| The evaluator | every one of the 20 models validated |
| Fuel | 1,488,437,746,353 the forty together; the most of one 490,332,441,060 (`lia0555`, 98 s of the reference machine, 20.2 s at the competition) |

The fuel is the same wherever the sample runs (a Windows x64 workstation and the build server's
Linux). Against the competition's seconds a problem took median 0.54 reference seconds, the most
7.2 (`bv0308`: 4.2 s there, 30 s here), the least almost none (`lia0749`: 46.5 s there, 0.2 s
here); the solver's memory peaked at 203 MiB.

## Measured on the test fleet

The first real run of `SCRIPT.md`, twice in a row. To be filled in after it.

| | Run 1 | Run 2 |
|---|---|---|
| Date, controller version | | |
| Nodes and their platforms | | |
| Wall time (`completed in`) | | |
| Verdicts: sat / unsat / unknown (by reason) | | |
| The evaluator's classes: validated / partial / failed | | |
| Decided by band (`check.py`): 1-2, 2-5, 5-10, 10-20, 20-40, 40-60 s | | |
| Fan-outs (main / repair / dispute): packages, complete, unanswered | | |
| Copies: compared, agreed; unsat on one machine | | |
| Fuel in all, the most of one problem (`check.py`) | | |
| Reference seconds per competition second, median (`check.py`) | | |
| `check.py` | | |
| `compare` | | |
