# smt-live

The live client of the Smt controller: a small console that drives the controller's bundled
scripts on a real server through the public SDK (`OutWit.Cloud.SDK`), the way a lab's
application does, and works with what they give; and that runs a problem set on one machine
with the controller's own code, the local side of the comparative test. A developer tool: it is
never packed or published.

| Verb | Needs a server | What it does |
|---|---|---|
| `diagnostics` | yes | runs `SmtRuntimeDiagnostics`; prints what the server's controller carries and can do, and the capabilities document, held to its schema |
| `preflight` | yes | uploads a problem set, runs `SmtPreflight`; prints what the set and the options come to; nothing is sent to a node |
| `run` | yes | uploads a problem set, runs `SmtRun`, waits with its progress, downloads the four documents, holds each to its schema, prints the summary |
| `fetch` | yes | the same for a job of `SmtRun` submitted earlier, by its id: waits while it runs, downloads, checks and summarises its documents |
| `compare` | no | two folders `run` wrote, for one problem set: the same settled answers and the same provenance, or every difference |
| `pack` | no | a folder of SMT-LIB files into a problem set, read back as the server reads it |
| `local` | no | a problem set on this machine alone, by the controller's own code: the four documents of a run and `local.json`, what it measured |
| `comparative` | no | a folder `local` wrote and a folder `run` wrote, for one set: the comparative table, the digests row by row, the crossover |

## Build

```
dotnet build -c Release
bin/Release/net10.0/smt-live --help
```

The schemas of the controller's documents (`../OutWit.Controller.AI.Smt/docs/smt-*.schema.json`)
are copied beside the tool, into `docs/`, by the build: a run's documents are held to the schemas
the controller publishes, not to a copy of the tool's own.

`local` solves with the controller itself, and the controller holds what it solves with to its
pins: the solver module (`wasm/z3-5.4.1.wasm`) and the native wasmtime library of the platform
(`runtimes/<rid>/native/`), both beside the controller's assembly, which lies beside the tool. A
plain build does not carry them (the binding's own library is left out: the controller would
refuse it). They are the controller's data assets, resolved from its manifest as a node's client
resolves them: each archive fetched from the release the manifest names, held to the SHA-256
and the size it states, and unpacked where it says. A publish for one platform does that (below);
so does a build with `-p:SmtLiveAssets=true`, for this machine's platform:

```
dotnet build -c Release -p:SmtLiveAssets=true
bin/Release/net10.0/smt-live local demo/02-eight-queens/problems.jsonl
```

The controller's types `local` calls are internal to it; the controller names the tool as a
friend (`InternalsVisibleTo("smt-live")`), so at run time the tool calls the very assembly a node
loads.

### A build for another machine

`publish-local.sh` makes a self-contained folder of the tool for one platform (no .NET needed
where it runs), with the controller's data assets of that platform beside it, checks that the
module and the library are the bytes the controller's source pins and that no other wasmtime
library is in the folder, and zips it (the zip keeps the tool executable for `unzip`). It runs
on Windows (Git Bash), Linux and macOS, for any of the three platforms:

```
bash publish-local.sh osx-arm64 demo-out/smt-live-osx-arm64    # or win-x64, linux-x64
```

The tool of an `osx-arm64` build is signed ad hoc by the .NET SDK, from any system; should macOS
kill it at its start ("Killed: 9"), sign it again on the Mac with `codesign --force --sign -
smt-live`, and never sign the native library again: its SHA-256 is pinned. The workflow "AI Smt"
publishes and runs it on each of the three platforms. How the comparative test takes it to a
Mac and back is in [`demo/04-comparative/SCRIPT.md`](demo/04-comparative/SCRIPT.md).

## Environment

`diagnostics`, `preflight`, `run` and `fetch` read where to connect from three variables. None
has a default; the tool names the ones that are not set.

| Variable | |
|---|---|
| `WITAI_SMT_LIVE_SERVER` | the server's URL |
| `WITAI_SMT_LIVE_IDENTITY` | the identity server's URL |
| `WITAI_VERIFY_LIVE_KEY` | a `wit_sk_` application key: the variable the Verify and Corpus live tools read too |

The key goes to the SDK and nowhere else: it is never printed or written. The key goes only to an
`https://` address: both addresses must be `https://` (the key is exchanged with the identity
server, and the token it gives goes to the server), or `http://` to this machine
(`http://localhost`, `127.0.0.1`, `[::1]`) for a local test. Any other address is refused, by the
variable's name and without its value, before a connection is made.

## The options of a run

`preflight` and `run` take the options of the controller's options document, by its names. An
option not given is the controller's default, and so is 0, but for `--rate`: `--rate 0` turns the
copies off (no package is solved on two machines, and every `unsat` rests on one machine; the
report says `sampling none`), while `--rate` not given is the default, 0.05.

| Option | Document name | Absent | 0 | Ceiling |
|---|---|---|---|---|
| `--query check\|enumerate\|prove` | `query` | `check` | | |
| `--rate R` | `sampling_rate` | 0.05 | no copies | 1 |
| `--rlimit N` | `rlimit` | 150,000,000 (the profile's) | the default | 4,294,967,295 |
| `--hard-cap S` | `hard_cap_s` | 60 | the default | 600 |
| `--memory MB` | `memory_mb` | 1,024 | the default | 4,096 |
| `--seed N` | `seed` | 0 | the default | 2,147,483,647 |
| `--max-models N` | `max_models` | 2 | the default | 1,000 |

`run` refuses options out of their ranges before anything is sent, and before it connects (the
controller would refuse the run); `preflight` sends them, and prints the controller's sentence for
each.

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

The folder of the documents (`smt-run-` and the tool's start, `yyyyMMdd-HHmmss`, when `--out` is
not given) is made first, and held to be writable and free: a folder that cannot be written, or
that already holds a run's documents (one of the four, `fleet.json` or `local.json`), is refused
before the tool connects, and nothing is sent. Then the set is uploaded as a blob (in chunks,
with its progress); `SmtRun` is submitted with the blob and the options (the job's id is printed);
the job's progress is printed while it runs. A status that cannot be read (a connection that
drops) is asked for again at every poll for up to ten minutes before the tool stops waiting. Once
the job has ended, its four documents are downloaded before anything else is done, each on its
own:

| File | Schema |
|---|---|
| `results.jsonl` | `smt-results.schema.json`, row by row |
| `report.json` | `smt-report.schema.json` |
| `integrity.json` | `smt-integrity.schema.json` |
| `provenance.json` | `smt-provenance.schema.json` |

Then the summary of the report and the integrity document: what the run ran with, the set as it
was read, the rows by status, the verdicts, the unknowns by reason, the agreement with the expected
verdicts, the evaluator's classes (by logic, and the partial ones by reason), the enumerations,
the copies and how they agreed, what rests on one machine (the problems, the `unsat` among them
and the complete enumerations that found models: `unsat_one_copy` and `complete_one_copy`), the
run's `binding` (whether the platform told the server which package each result came back for,
with the results told and not), the disputes and how they ended, the lies found, the
disagreements between the evaluator and the solver (each by index and id), the problems without
a result, the repairs, refused result packages and node faults, each fan-out, and the first rows
that were not answered `ok`. The report is held to the rows it was written from: their number,
and the counts by status, verdict, agreement with the expected verdict and the evaluator's class.

When the tool cannot finish its part after the submit, the job goes on on the server, and the
tool says so with the line that fetches its documents once it has ended
(`smt-live fetch <job id> --out <folder>`): on Ctrl+C, when the job's status could not be read for
ten minutes, and when a document could not be downloaded (the others are downloaded all the same,
and the line names a fresh folder). A job that failed or was cancelled, or a stop before any
document came, leaves no empty folder behind.

`run` also writes `fleet.json` beside the documents: what the run took, measured end to end from
the tool's start (the moment the files were ready) to the documents on disk, the fleet side of
the comparative test:

| Field | |
|---|---|
| `schema` | `smt-live-fleet/1` |
| `started_utc`, `job`, `status` | when the tool started, the job, how it ended |
| `problem_set` | the set's file name, SHA-256, bytes and rows, and the rows taken (from the report) |
| `seconds.connect` | the tool's start to the server connected and signed in |
| `seconds.upload` | describing and uploading the set |
| `seconds.job` | the submit to the poll that saw the job end: the server cut the set into packages, the fleet solved them, the server settled the run (every `sat` model through the evaluator) and wrote the documents. Its error is the poll's interval (`poll_seconds`) |
| `seconds.fetch` | downloading the four documents and holding each to its schema |
| `seconds.wall` | the tool's start to the four documents on disk |
| `server` | the job's times by the server's clock, when it gives them: `queue_seconds` (taken to started) and `run_seconds` (started to ended) |
| `problems_per_second` | the rows taken over the wall time |

A line that starts with `!!!` is one a lab must not miss, and every such line of the summary makes
the run **alarming** (exit 3): a disagreement between the evaluator and the solver, a lie, a
standing dispute, a problem without a result, a verdict that disagrees with the expected one, a
settled model the evaluator calls failed (each counted from the report and from the rows: a row
says it whatever the report counts), a sampling that was asked for and did not happen (`short`),
and a report that does not count the rows it was written from. A package a fan-out left
unanswered is printed on its fan-out's line, not loudly: the repair round answers it, and a
problem left without a result is an alarm of its own.

### fetch

```
smt-live fetch <job id> [--out <folder>]
```

The job of a `run` that the tool stopped waiting for (Ctrl+C, a connection lost, a download that
failed), by the id `run` printed. Its status is read once (a job the server does not know, or not
for this key, is said at once, and a job of another script is refused); while it runs, its
progress is printed; once it has ended, its four documents are downloaded, checked and summarised
exactly as `run` does, with the same exit codes. The folder (`smt-run-` and the head of the job's
id when `--out` is not given) is held as `run` holds it. A fetch measures nothing: it writes no
`fleet.json` (the tool did not see the run from its start).

### compare

```
smt-live compare <folder-a> <folder-b>
```

Two runs of the same problem set. Every row of the results must be the same by every field but
the three that depend on where the work was placed (`copies`, `agreed`, `settled_by`): the status,
the verdict, the models, the values, the validation, the digest, the stats. The provenance must be
the same as a whole: the profile, the module, the engine, the controller, the options, the set.
Both files are read a row at a time. Every difference is printed with its place (a row that is
JSON but no object is one); any makes the exit code 4.

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

### local

```
smt-live local <problems.jsonl> [options but --rate] [--degree N] [--out <folder>]
```

The set runs on this machine alone, by the controller's own code, from the tool's start to the
four documents on disk; nothing is sent anywhere:

1. **The sandbox**: the controller's factory makes it from the module the controller's locator
   finds (beside the controller, or the file `WITAI_SMT_Z3` names), holds the module to the
   profile's size and SHA-256 and the native library to the natives guard's SHA-256, and
   compiles the module (tens of seconds). A module or a library of other bytes gives no sandbox,
   and the run stops with the controller's reason.
2. **The set**: read and cut into packages by the code `Smt.Split` runs on the server: the same
   reader and sanitizer, the same refusals, the same packages with their tickets.
3. **The degree**: the node's rule, `min(cores, 16, 75 % of the available memory / one session)`,
   read once after a collection, so the view of the memory is fresh; or the sessions `--degree`
   gives (1 to 256), with the rule's degree beside it.
4. **The solve**: each package checked as a node checks it; every problem solved by the node's
   session code on the node's worker threads (below normal priority), its row made and its digest
   computed by the node's code, and written to its package's result package. One pool goes over
   the whole set in its order, as a queue of files would on one machine. A node since controller
   0.5.0 does the same with the packages of its share: they share one budget of sessions, and a
   session that ends goes to the next problem waiting, of the same package or of the next one
   (before, a node solved one package at a time and waited for each package's slowest problem).
   What is left between the two is a node's view of its share a few packages at a time: its
   packages are let in as the open ones have handed out all of their problems, at most sixteen
   open at once.
5. **The settlement and the documents**: by the code `Smt.Settle` and the documents' activities
   run on the server: every `sat` model checked by the host's evaluator, the four documents
   written by the host's writers and held to their schemas, and the summary printed as `run`
   prints it.

One machine makes no copies, so the fields only a fleet has say what is true here: the sampling
rate is 0 (`--rate` is not an option of `local`; the report says `sampling none`), every answer
rests on one copy (`copies` 1, `agreed` true, `settled_by` `one_copy`; every `unsat` is in
`unsat_one_copy`, every complete enumeration that found models in `complete_one_copy`), no platform says which package a result came back for (`binding` `unbound`),
and there is no repair or dispute round (a problem without an answer stays without one). A row's digest and its stats do not depend on any of that: they are the same as a
fleet's for the same set, options and module.

The folder (`smt-local-` and the start's time when `--out` is not given) gets the four documents
and `local.json`; it is held as `run` holds its folder, before the sandbox is made (one that holds
a run's documents is refused). The working files (the packages, the result packages, the
sandbox's stdio files) live in its `.work` folder while the run lasts and are removed at its end.
Ctrl+C stops the run between problems: no problem is handed out after it, the sessions that run
end at their check's end, and no document is written; a second Ctrl+C ends the tool at once (its
`.work` folder then stays, and the next run in the folder removes it).

| `local.json` | |
|---|---|
| `schema`, `started_utc` | `smt-live-local/1`; when the tool started |
| `machine` | `os`, `os_version`, `platform`, `architecture`, `cores` (logical), `memory_bytes`, `available_bytes`, as the runtime sees them; no name of the machine, a user or a folder |
| `degree` | `sessions` and `decided_by` (`cores`, `cap`, `memory`, or `given`); `rule_sessions` and `rule_decided_by`, what the node's rule gives here; `session_bytes` |
| `solver` | the profile and the controller's version; `module` (its place beside the tool, or that `WITAI_SMT_Z3` named it), `module_sha256`, `module_pinned`; `engine`, `natives_sha256`, `natives_pinned`, as loaded; `explicit_traps` |
| `problem_set` | the set's file name, SHA-256, bytes, rows, taken and refused |
| `packages` | the packages the set was cut into |
| `seconds.startup` | the tool's start to the sandbox ready: the runtime's start and the module's compile |
| `seconds.read` | reading the set and cutting it into packages |
| `seconds.solve` | the pool: the first problem handed out to the last row written |
| `seconds.settle` | settling (the evaluator on every `sat` model) and writing the documents |
| `seconds.wall` | the tool's start to the four documents on disk |
| `problems_per_second` | the rows taken over the wall time |

What `local` does not measure: copying the tool and the set to the machine (the files are ready
when the tool starts), and anything a fleet adds: transfers, a queue, other machines.

### comparative

```
smt-live comparative <local folder> <fleet run folder> [--fixed <fleet run folder of a small set>]
```

The comparative table of one set, from what `local` and `run` wrote; every number is read from
their documents and measurements, and nothing is estimated. A measurement (`local.json`,
`fleet.json`) is read only beside the documents of the set it measured: one whose set is not the
set its folder's provenance names is refused (exit 2).

- **The same thing?** The set (its SHA-256 in both provenances), the module, the options but the
  sampling rate, and both controller versions.
- **The table**: each side's wall time end to end and its parts (start-up, read, solve, settle;
  connect, upload, job with the server's queue and run, fetch), the problems taken and a second,
  the statuses, the verdicts, the agreement with the expected verdicts, the evaluator's classes
  and the failed validations, the sampling, the answers taken, the copies that disagreed, the
  retries (repairs and disputes), the node faults and the refused results, the unanswered
  packages and the problems without a result, the lies and the evaluator's disagreements.
- **The rows**, side by side: equal digests (`smt-result/1`: the answer, the models, the values,
  the validation's inputs and the cost, everything but where and when) are the strongest sign that
  both sides ran the same problem with the same module under the same options. A row without a
  digest is compared by its status and code. A flip is a row whose verdict differs. The first 20
  differences are printed with their index and id.
- **The crossover**: each side's time is a fixed cost and a cost a problem, the same for every
  problem of a set like this one. This machine's fixed cost is its start-up (measured), its cost
  a problem the rest of its wall time over its problems. The fleet's line goes through two fleet
  runs measured end to end: a small set's (`--fixed`, whose time is almost all fixed cost) and the
  compared set's. Where the lines cross is the size above which the fleet finishes first; the
  lines, the inputs and these assumptions are printed. "This machine finishes first at every
  size a set may have" when the fleet never catches up, or catches up only above the 1,000,000 rows
  a set may have; "the fleet finishes first at every size a set may have" when this machine
  catches up only above them. The lines are drawn only from two runs that ran the same thing and
  agree row by row, and from a `--fixed` run that completed and ran the fleet run's module with
  its options and its sampling rate; otherwise, as without `--fixed` or without `fleet.json` (a
  run of an older tool), there is no crossover, and the line says why.
- **Operator effort** is not measured by the tool: `SCRIPT.md` of the comparative test lists each
  side's steps.

Exit 0 when both ran the same set, module and options and every row is the same; 4 when anything
differs; 2 when a document is not there or a measurement is not of its folder's set.

## Exit codes

One meaning each:

| Code | |
|---|---|
| 0 | done, and everything held |
| 1 | the work was done and did not hold: a job did not complete; a document did not come or does not hold to its schema; a preflight would not start or refuses the options; a packed file was skipped or its row is refused; a local run had no sandbox |
| 2 | the command line, an input, the folder or the environment is wrong (an address the key may not go to among them); nothing was sent or run |
| 3 | a run, a fetched run or a local run completed and its documents hold to their schemas, and it reports something alarming |
| 4 | the two runs of a `compare` or a `comparative` differ |
| 5 | stopped by Ctrl+C; the tool said what it stopped and, for a job, that it goes on on the server and how to fetch it |
| 6 | the tool could not finish: the connection, the server or this machine failed (a sign-in, an upload, a status unread for ten minutes, a download, a file); what is left on the server is said |

Ctrl+C stops the tool's work at the first press and ends the tool at the second.

## Demo kits

- [`demo/01-known-answers`](demo/01-known-answers/SCRIPT.md): the 469 SMT-LIB problems of the
  controller's slice tests, fetched by the tests' own list and fetcher and packed, the set held to
  its SHA-256; every verdict must be the list's and every class of the evaluator the slice test's.
- [`demo/02-eight-queens`](demo/02-eight-queens/SCRIPT.md): two enumerations (eight queens, 92
  models; a Latin square with exactly one), the smoke test of a fleet.
- [`demo/03-long-tail`](demo/03-long-tail/SCRIPT.md): the long tail, 1,960 SMT-LIB problems of
  QF_LIA and QF_BV that took Z3 from 1 to 60 seconds at past SMT competitions, chosen from the
  SMT-LIB Catalog by a committed script and fetched by their list; every verdict must be the
  problem's `:status` or an unknown at a budget's end, and 95 % of the models or more validated.
  The workflow "AI Smt long tail" reads every row of it and solves its sample.
- [`demo/04-comparative`](demo/04-comparative/SCRIPT.md): the comparative test: one set on one
  machine (`local`) and on the fleet (`run`), and the table of the two (`comparative`).

What fetches and runs write goes to `demo-out/`, which is never committed.

## Tests

```
dotnet test Tests/OutWit.Controller.AI.Smt.Live.Tests.csproj
```

They need no server: the command line, the ids and the packing of a folder (read back by the model
package's reader), the schema check against the controller's own schemas, the summary, the
comparison of two runs, the four server verbs driven through a server in memory (what `run`
measures, and how it ends when it is stopped, loses the server or cannot download a document),
the waiting for a job against the SDK's jobs in memory, the exit codes, the eight-queens kit against the model evaluator (every placement of eight queens, one a
row, and the one completion of the Latin square), the long-tail kit's list against the rules of its
selection, its `EXPECTED.md` and the options of its commands, the comparative table and its
crossover on fixtures, and local runs. Those solve in the controller's sandbox, with the solver
module the controller's tests run (their `@Data/wasm`, found through `WITAI_SMT_Z3`) and the pinned
native library laid out by the asset tool's `natives.sh`, and are ignored without them (failed on
the build server): the eight-queens kit gives the rows the test fleet gave (their digests and costs
in its `EXPECTED.md`), and a handful of problems of every query the same rows at one session and at
three. A test of the category `Slice` runs `local` on eight problems of the slice the controller's
tests fetch, each with the list's verdict. The test project lives in the tool's folder; it is in
the solution, and the workflow "AI Smt" runs it on Windows, Linux and macOS after the controller's
tests (the slice test in its job "slice"), and runs the self-contained build of each platform on
the eight-queens kit from where its zip was unpacked. The Python scripts of the known-answers and
the long-tail kits have tests of their own in `demo/01-known-answers/tests` and
`demo/03-long-tail/tests` (`python -I -m unittest discover -s tests -t tests` in the kit's
folder), which the workflows "AI Smt" and "AI Smt long tail" run.
