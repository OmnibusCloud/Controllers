# smt-live

The live client of the Smt controller: a small console that drives the controller's bundled
scripts on a real server through the public SDK (`OutWit.Cloud.SDK`), the way a lab's
application does, and works with what they give. A developer tool: it is never packed or
published.

| Verb | Needs a server | What it does |
|---|---|---|
| `diagnostics` | yes | runs `SmtRuntimeDiagnostics`; prints what the server's controller carries and can do, and the capabilities document, held to its schema |
| `preflight` | yes | uploads a problem set, runs `SmtPreflight`; prints what the set and the options come to; nothing is sent to a node |
| `run` | yes | uploads a problem set, runs `SmtRun`, waits with its progress, downloads the four documents, holds each to its schema, prints the summary |
| `compare` | no | two folders `run` wrote, for one problem set: the same settled answers and the same provenance, or every difference |
| `pack` | no | a folder of SMT-LIB files into a problem set, read back as the server reads it |

## Build

```
dotnet build -c Release
bin/Release/net10.0/smt-live --help
```

The schemas of the controller's documents (`../OutWit.Controller.AI.Smt/docs/smt-*.schema.json`)
are copied beside the tool, into `docs/`, by the build: a run's documents are held to the schemas
the controller publishes, not to a copy of the tool's own.

## Environment

`diagnostics`, `preflight` and `run` read where to connect from three variables. None has a
default; the tool names the ones that are not set.

| Variable | |
|---|---|
| `WITAI_SMT_LIVE_SERVER` | the server's URL |
| `WITAI_SMT_LIVE_IDENTITY` | the identity server's URL |
| `WITAI_VERIFY_LIVE_KEY` | a `wit_sk_` application key: the variable the Verify and Corpus live tools read too |

The key goes to the SDK and nowhere else: it is never printed or written.

## The options of a run

`preflight` and `run` take the options of the controller's options document, by its names; an
option not given, or 0, is the controller's default.

| Option | Document name | Default | Ceiling |
|---|---|---|---|
| `--query check\|enumerate\|prove` | `query` | `check` | |
| `--rate R` | `sampling_rate` | 0.05 | 1 |
| `--rlimit N` | `rlimit` | 150,000,000 | 4,294,967,295 |
| `--hard-cap S` | `hard_cap_s` | 60 | 600 |
| `--memory MB` | `memory_mb` | 1,024 | 4,096 |
| `--seed N` | `seed` | 0 | 2,147,483,647 |
| `--max-models N` | `max_models` | 2 | 1,000 |

`run` refuses options out of their ranges before anything is sent (the controller would refuse
the run); `preflight` sends them, and prints the controller's sentence for each.

## The verbs

### diagnostics

```
smt-live diagnostics [--out <folder>]
```

Prints the controller's version and profile, the solver module's pin and whether the server's
process has it, the engine library and whether it is the pinned one, the platform and the trap
mode, and the degree a package would get there; then the capabilities document: held to
`smt-capabilities.schema.json`, saved as `<folder>/capabilities.json` with `--out`, and printed in
short (the defaults and ceilings, the platforms, the pool as totals).

### preflight

```
smt-live preflight <problems.jsonl> [options]
```

Prints the set's SHA-256 and the rows taken and refused (by code, and the first refusals in full:
the row, the id, the line in the problem, the command, the code, the reason), the taken problems
by logic, query and expected verdict, the commands the sanitizer leaves out, the licences, the
options as the controller resolves and checks them, the memory a node needs for one session, the
upper bound of the work (the taken problems times the hard cap: a bound, not an estimate) and the
packages a run would make. Exits 1 when a run would not start or the options are refused.

### run

```
smt-live run <problems.jsonl> [options] [--out <folder>]
```

The set is uploaded as a blob (in chunks, with its progress); `SmtRun` is submitted with the blob
and the options; the job's progress is printed while it runs. The documents go to the folder
(`smt-run-` and the head of the job's id when `--out` is not given):

| File | Schema |
|---|---|
| `results.jsonl` | `smt-results.schema.json`, row by row |
| `report.json` | `smt-report.schema.json` |
| `integrity.json` | `smt-integrity.schema.json` |
| `provenance.json` | `smt-provenance.schema.json` |

Then the summary of the report and the integrity document: what the run ran with, the set as it
was read, the rows by status, the verdicts, the unknowns by reason, the agreement with the expected
verdicts, the evaluator's classes (by logic, and the partial ones by reason), the enumerations,
the copies and how they agreed, the problems that rest on one machine, the disputes and how they
ended, the lies found, the disagreements between the evaluator and the solver (each by index and
id), the problems without a result, the repairs, refused result packages and node faults, each
fan-out, and the first rows that were not answered `ok`.

A line that starts with `!!!` is one a lab must not miss. These make the run **alarming** (exit 3):
a disagreement between the evaluator and the solver, a lie, a standing dispute, a problem without
a result, a verdict that disagrees with the expected one, a settled model the evaluator calls
failed, and a sampling that was asked for and did not happen (`short`).

### compare

```
smt-live compare <folder-a> <folder-b>
```

Two runs of the same problem set. Every row of the results must be the same by every field but
the three that depend on where the work was placed (`copies`, `agreed`, `settled_by`): the status,
the verdict, the models, the values, the validation, the digest, the stats. The provenance must be
the same as a whole: the profile, the module, the engine, the controller, the options, the set.
Both files are read a row at a time. Every difference is printed with its place; any makes the
exit code 1.

### pack

```
smt-live pack <folder> [--query check|enumerate|prove] --out <problems.jsonl>
```

One row a `*.smt2` file of the folder and its subfolders, in the ordinal order of their paths.
The id is the file's relative path without `.smt2`, with `/` between its parts, made a valid id
(every character that is not a letter, a digit or one of `. _ - / + : @ =` becomes `_`); a path
longer than 64 characters, or one whose id an earlier file took, keeps its tail behind the first
eight hex digits of the SHA-256 of the whole path and `=`. The text goes into the row as the file
has it; `expected` is not written: the controller reads it from the problem's `:status`.

| `--query` | The rows |
|---|---|
| none | name no query: a row takes the run's |
| `check` | name it |
| `enumerate` | name it and project on every constant the problem declares (`declare-const`, `declare-fun` of no arguments); the number of models is the run's |
| `prove` | name it; the problem's last `assert` is the goal, and the problem is the rest |

The set is then read back with the model package's reader, the code the server reads it with:
every row it refuses is printed as the server would refuse it, with the sanitizer's own reason
when the query could not be given what it needs. A file that is not UTF-8, or larger than a row of
a set may be (16 MiB), is skipped and said. The set is written in any case; the exit code is 1
when a file was skipped or a row is refused.

## Exit codes

| Code | |
|---|---|
| 0 | done, and everything held |
| 1 | a job did not complete; a document did not come or does not hold to its schema; a preflight would not start or refuses the options; two runs differ; a packed file was skipped or its row is refused |
| 2 | the command line, an input or the environment is wrong; nothing was sent |
| 3 | a run completed and its documents hold to their schemas, and it reports something alarming |

## Demo kits

- [`demo/01-known-answers`](demo/01-known-answers/SCRIPT.md): the 469 SMT-LIB problems of the
  controller's slice tests, fetched by the tests' own list and fetcher and packed; every verdict
  must be the list's and every class of the evaluator the slice test's.
- [`demo/02-eight-queens`](demo/02-eight-queens/SCRIPT.md): two enumerations (eight queens, 92
  models; a Latin square with exactly one), the smoke test of a fleet.

What fetches and runs write goes to `demo-out/`, which is never committed.

## Tests

```
dotnet test Tests/OutWit.Controller.AI.Smt.Live.Tests.csproj
```

They need no server and no solver: the command line, the ids and the packing of a folder (read
back by the model package's reader), the schema check against the controller's own schemas, the
summary, the comparison of two runs, the three server verbs driven through a server in memory,
and the eight-queens kit against the model evaluator (every placement of eight queens, one a row,
and the one completion of the Latin square). The test project lives in the tool's folder; it is
in the solution, and the workflow "AI Smt" runs it on Windows, Linux and macOS after the
controller's tests.
