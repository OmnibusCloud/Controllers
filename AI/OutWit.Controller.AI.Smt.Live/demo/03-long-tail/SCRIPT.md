# 03 - The long tail: SMT-LIB problems that took Z3 from 1 to 60 seconds

Kit A of the controller: @@PROBLEMS@@ non-incremental problems of the SMT-LIB release 2025, @@LIA@@ of
linear integer arithmetic (QF_LIA) and @@BV@@ of bit-vectors (QF_BV), each with the verdict its
authors gave it in its `:status` (@@SAT@@ `sat`, @@UNSAT@@ `unsat`), each of which took Z3 between 1 and 60
seconds at a past SMT competition. The problems of `01-known-answers` are decided in milliseconds;
these take seconds to minutes each, so the work is the long tail, and what a run shows is how a
fleet shares it out: every verdict agrees with the known one or is an unknown at a budget's end,
every `sat` model is checked by the server's evaluator, a share of the problems runs on two
machines and their answers are compared, and the report's long tail lists every problem by the
fuel it took.

The list names the problems by path, size and SHA-256; the problems are fetched from the
collection's archives and never committed.

## Where the times come from

The **SMT-LIB Catalog 2025** (Zenodo record 16290040, DOI
[10.5281/zenodo.16290040](https://doi.org/10.5281/zenodo.16290040), published 2025-07-21, CC BY
4.0): an SQLite database of the library's metadata and of the results of every SMT-COMP since
2005, made by the SMT-LIB maintainers and described in "Exploring the SMT-LIB Benchmark Library"
(TACAS 2026). Pinned by hash:

| File | Bytes | SHA-256 |
|---|---|---|
| `SMT-LIB-Catalog-2025.tar.zst` (Zenodo's MD5 `50b0ff03107a4f0c3274d48d2b556705`) | 1,199,613,453 | `15c7ba8e6e47f36d3851a437e0e0f1dbac3238f7c73d07fe25b377513d98c3c6` |
| `smtlib2025.sqlite` inside it | 5,830,533,120 | `f6d26a67996c006dac2933b93cbe2d96484603c75e8bc27b6bdb1a628f5ea0c9` |

**The time of a problem is Z3's own**: the CPU time of the solver Z3 (not Z3alpha, Z3++ or the
other solvers built on it, which the catalog lists apart) at the newest SMT-COMP in which Z3 ran on
the problem, the fastest of Z3's entries there that answered the problem's `:status`. One rule for
every problem, and the solver the controller runs: the time says how hard the problem is for Z3,
which is what the options below must fit, and not how hard it is for the fastest solver of the
year. For these two logics Z3 ran at every SMT-COMP from 2014 to 2022 (Z3 4.3.2 to 4.8.17; SMT-COMP
2018 ran the whole library), all on StarExec's machines of 2.4 GHz (one hardware revision in the
catalog from 2013 to 2023). A problem Z3 did not solve at its newest run, or answered wrongly
there, has no time and is not taken; nor is one whose declared status the catalog's status
inferred from all the competitions' answers contradicts.

**Matching.** The catalog names a problem by its family's folder and its path below it, the way
the release lays its archives out (`non-incremental/<logic>/<folder>/<path>`); the catalog of 2025
describes the release of 2025, so a problem renamed by an earlier release is already under its
2025 name there. `make_list.py` holds each candidate to the release's archive anyway: the file
must be there, of the size the catalog gives, declaring the same `:status`. @@MATCHED@@

## The selection

`make_list.py` (the rules are its constants, the list is its output, and the same two pinned inputs
give the same list byte for byte):

- QF_LIA or QF_BV, non-incremental, one `check-sat`, `:status` `sat` or `unsat`;
- Z3's time, by the rule above, from 1 to 60 seconds;
- a licence that lets a lab fetch, run and report the problem: CC BY 4.0 (the collection's
  default), CC BY-SA 4.0, CC0, MIT, Apache 2.0, ISC; not CC BY-NC, not GPL, not unnamed;
- **at most 512 KiB a file.** The controller's reader takes a problem of up to 4 MiB of characters
  and 2,000,000 tokens, a row of a set up to 16 MiB, a package of problems up to 32 MiB of text.
  An eighth of the reader's limit is far inside all of them, keeps the set near a quarter of a
  gigabyte, which a lab uploads once and the fleet's packages carry again (twice for a sampled
  one), and is the cap of the controller's slice of the same release. Larger files exist: QF_BV
  has problems of hundreds of megabytes, and 1,740 of its candidates in the band are above 512 KiB;
- **at most 200 problems of one family** (a tenth of the kit): a family is one generator or one
  application, and QF_LIA's candidates are two thirds one family (`nec-smt`);
- spread: each logic is filled in rounds, and every round takes one more problem, in a fixed
  pseudo-random order (SHA-256 of a seed and the path), from every cell of (family, status, time
  band) that has one left, while the family is under its cap. Every family gives its share before
  a large one gives more, both statuses within a family, every band of 1-2, 2-5, 5-10, 10-20, 20-40
  and 40-60 seconds;
- QF_LIA takes up to 1,000, QF_BV fills the kit up to 2,000. @@SHORT@@

The list marks ten problems of each logic and status, at evenly spaced places of their times, as
the sample that the build server solves (`SmtLongTailSampleTests`, workflow "AI Smt long tail").

## Files

- `kit-list.tsv`: the list; per problem its id (`lia0000` ..., `bv0000` ..., in the order of the
  paths), logic, path in the release's archive, size, SHA-256, `:status`, Z3's CPU and wall time
  at the competition and which competition and entry (`time_source`), licence, category, family,
  time band and whether it is of the sample.
- `make_list.py` writes the list from the catalog and the archives (below).
- `make_set.py` fetches the listed files and packs them: it streams the archives of the two logics
  from Zenodo once each (QF_LIA 687 MB, QF_BV 1.7 GB; it needs the `zstd` tool on the PATH or a
  Python with a zstd module), keeps the listed files, holds each to its SHA-256, writes them as
  `<work>/problems/<id>.smt2`, writes `<work>/ATTRIBUTION.tsv` from each file's own `:license`,
  `:category` and `:source`, copies the sample to `<work>/sample/`, and packs the problems with
  `smt-live pack` into `<work>/problems.jsonl`, every row of which the server's reader must take. A
  file already fetched is not fetched again.
- `check.py` holds a run to what `EXPECTED.md` says must be so on any fleet, and prints the numbers
  only a fleet gives.
- `tests/`: the tests of `make_list.py` and `check.py` (`python -I -m unittest discover -s tests
  -t tests`), on made-up catalogs, archives and runs.
- `EXPECTED.md`: what a run must show, and the table of the first fleet run.

The work folder holds the fetched problems: it is never committed (`demo-out/` is ignored).

## Run

From `AI/OutWit.Controller.AI.Smt.Live`, with the environment of the README set:

```
dotnet build -c Release
python -I demo/03-long-tail/make_set.py demo-out/long-tail
bin/Release/net10.0/smt-live preflight demo-out/long-tail/problems.jsonl @@OPTIONS@@
bin/Release/net10.0/smt-live run demo-out/long-tail/problems.jsonl @@OPTIONS@@ --out demo-out/long-tail/run-1
python -I demo/03-long-tail/check.py demo-out/long-tail/run-1
bin/Release/net10.0/smt-live run demo-out/long-tail/problems.jsonl @@OPTIONS@@ --out demo-out/long-tail/run-2
bin/Release/net10.0/smt-live compare demo-out/long-tail/run-1 demo-out/long-tail/run-2
```

## The options

@@OPTIONS_WHY@@

## Making the list again

```
python -I make_list.py <smtlib2025.sqlite> <work folder>
```

The catalog comes from `https://zenodo.org/records/16290040/files/SMT-LIB-Catalog-2025.tar.zst`
(unpack it with `zstd -dc ... | tar -x`; about 7 GB unpacked); the script holds the database to its
SHA-256 first. It streams the two archives once and keeps a table of the candidates' sizes, hashes
and statuses in the work folder (`QF_LIA-members.tsv`, `QF_BV-members.tsv`), which a second run
reads instead of streaming again; `--archive QF_BV=<path>` reads a local copy of an archive. It
prints, for each logic, how many benchmarks each rule left out, how many candidates matched the
release, and the list's numbers by status, family, band and size.

## What to look at

1. **The preflight**: every row taken; QF_LIA and QF_BV; the expected verdicts; the options as
   numbers; the upper bound (the problems times the hard cap: a bound, not an estimate).
2. **The run**: the progress; the long tail of the report, every problem by the fuel it took,
   those stopped by a budget marked.
3. **The summary**: the verdicts and the unknowns by reason; every expected verdict agreed with
   or left unknown; the evaluator's classes; the copies of the sampled packages and their
   agreement.
4. **check.py** prints `held: everything`, the verdicts by competition band, and the fuel against
   the competition's time.
5. **compare**: the two runs give the same settled answers and the same provenance (fuel is a
   count: a problem stopped by the hard cap is stopped at the same point on every machine).
