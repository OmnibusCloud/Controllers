# OutWit.Controller.AI.Verify

Sandboxed, deterministic, resource-metered execution of untrusted programs
against specifications — on anonymous volunteer nodes. One primitive
(`Verify.ExecutePackage`) carries several products: code-vs-tests verification
(RLVR-style reward checking), code-corpus generation with checking, and math
problem generation with CAS checking — generation and verification are the
same activity with different programs.

**Status: 1.0.0 (Verify.Model 1.0.0, Verify.Scripts 1.0.0) — the first release; not on
nuget.org or the live network yet.** Its code is the code of 0.17.0, which ran on the test
fleet on Windows x64, Linux x64 and macOS arm64 nodes, with a node that lied and one that
answered nothing among them; 1.0.0 changes the version and nothing a node or the server runs.
What 1.0.0 is, after the 0.x line on the test server, is under "What changed in 1.0.0" below.

What the controller can do, for an application built on it:
[docs/capabilities.md](docs/capabilities.md).
`VerifyRun` returns a byte-identical verdict report for a taskset across runs,
placements and fleets, as long as every node did its work and told the truth: a
million small tasks gave the same report on six nodes (22m41s) and on four
(41m35s), measured before 0.13.0. Every node executes batches at a bounded degree
below normal priority and reports the delivery and the progress of its share;
`VerifyGenerateAndCheck` runs the generate-then-check shape end to end.

Isolation targets WASM (wasmtime): deterministic, cross-platform
(Windows / Linux / macOS, x64 / arm64), no admin rights or Docker required on
contributor machines, no ambient authority — network and filesystem do not
exist inside the sandbox unless explicitly virtualized in. Verdicts are
verifiable by construction: code passes its test suite or it does not.

## What changed in 1.0.0

1.0.0 is the 0.x line of the test server made one release. A lab that ran 0.17.0 sees the same
verdicts, fuel figures, outputs and documents (`verify-report/3`, `verify-provenance/2`,
`verify-integrity/3`, `verify-capabilities/3`), the same scripts and the same wire types;
the documents name the controller version 1.0.0. What the 0.x releases made of the controller,
in the words of what a lab can count on:

- **Fuel is the one budget of a run's work.** `Timeout` means out of fuel and nothing else, and
  it is the same on every node. The wall clock is a node's own backstop and gives no verdict: a
  run it ends is sent out once more and is `NoResult` (`wall_clock`) if no node does better,
  wherever in the run the clock fired. Every call into the host costs 1,000 units of fuel,
  whoever answers it, besides the unit a byte it moves; a wait (`poll_oneoff`) costs the bytes
  of its records, and the arguments and the environment cost their bytes each time a program
  copies them. A program that does nothing but wait runs out of fuel. Output is capped at the
  write: a byte past a cap is never stored, in memory or on disk.
- **What a task's own module may cost a node is bounded before it is compiled.** A module a task
  brings, and a program built from its sources, is held to bounds: a function type has at most
  128 parameters and 8 results, a block passes at most 8 values, a function has at most 16,384
  locals and a body of 128 KiB, a module at most 32,768 functions and 4,194,304 declared locals,
  blocks nest at most 2,048 deep, and a module with a start function is refused. A module past
  a bound is a `CompileError` with fixed words. What is within the bounds is priced from the
  task's fuel by what the engine was measured to take for its shape, and the memory the engine
  takes while it compiles is counted where a node decides how many tasks run at once. A C/C++
  build has one budget for everything it writes, its objects and its program together: 128 MiB.
- **What the server reads from a lab is held to its schema and bounded where it is read.** A
  field a taskset's schema does not have (a misspelt `expected_stdout`), a field given twice, a
  value of another type, a number outside its range, a NUL in a program or an argument and text
  that is not Unicode are refused by name, at the preflight and at the split; a runtime id is
  matched to the letter. The bounds: a line (64 MiB), the sources, arguments and cases of a task
  (1,024 each), the tasks of a taskset (4,194,304), and what a preflight answers (32 messages of
  a kind, 240 characters each). The capabilities document carries these bounds as numbers, in a
  section of their own (`taskset`), and is `verify-capabilities/3` for it. Nothing that grows
  with the taskset is read whole into memory: a run of 100,000 tasks from a 202 MiB taskset held
  45 MiB at the most on the managed heap, server and nodes together.
- **What the server takes from a node.** Tasks and results travel as blobs. A result package is
  accepted only under the ticket of the package it answers, from the server's own controller
  version, engine and runtime bytes, for exactly that package's tasks. It is walked before it is
  decoded, and every result is held to its task: fuel within the budget of its runs, memory
  within its cap, no time below zero, stdout within its cap, the case records of its suite and
  the verdict they give. Anything else is refused and counted by reason (`rejected_results`),
  and fails that package, never the run. A node, for its part, holds a package to the caps and
  the ceilings whatever the package says, and refuses whole one it cannot run.
- **The server never chooses between two executions.** A share of the packages
  (`SamplingRate`) runs twice on two machines; executions that agree are accepted, executions
  that disagree run three more times on different machines, and a result two of those three
  return, with no tie, is accepted; otherwise the task is `Disputed`. A task nobody answered
  goes out once more under the same sampling, and is `NoResult` with a reason if it still has
  no verdict. A run whose checking came back short says so: both summaries state the rate that
  was asked for (`sampling_rate`), whether the sampling took place (`sampling`: `as_asked`,
  `short`, `none`, `not_stated`) and the packages nothing answered (`unanswered_packages`).
- **What one machine can and cannot do.** A task that was not sampled rests on the one machine
  that ran it, and the documents count those (`single_execution`): a run that asks for no
  second execution rests on one machine for every task. Where the platform holds a node to one
  answer for each package it was given (Grid 1.3.0 and later), one lying machine can keep a
  dispute from being settled (`too_few_copies` or `surplus_copies`, the task `Disputed`), and it cannot make its
  answer the accepted one. Two lying machines that hold two of a task's three copies are the
  majority, and win.
- **A program sees the same world on every node:** the same file names, file records that say
  nothing of the host, the run's own clock, a random stream seeded by the task. Nothing of a
  node's own words reaches a lab: a node's trouble travels as fixed words or a reason code, and
  a node's log that fails decides nothing.
- **What the module hands on, and what guards its way to a node.** Every folder a node is sent
  has a `THIRD-PARTY-NOTICES.txt`; the archives are packed the same way every time and a job
  builds each again from its pins; a download without a pin stops a toolchain build.
- **On arm64 as on x64.** On a macOS arm64 node, with every task of the kits on two machines and
  the other copy on a Linux x64 node, no pair of copies differed, every verdict was the one x64
  gives, and the fuel of every batch was the same number on both: a budgeted task does not
  change its verdict between the two architectures. The compile prices hold there too: they
  were measured on an x64 Windows desktop and on an arm64 macOS laptop, and for the shapes that
  were measured a budget buys about as much compiling on both, to within a few times either way
  (the arm64 machine compiled them in 0.28 to 1.27 times the x64 time, 0.71 at the median), so
  no price depends on the architecture. They are not a proof about every module.

The limits that stay, each stated where it belongs below:

- a task that was not sampled rests on one machine (see "Integrity");
- one lying machine can leave a dispute unsettled and cannot take a result; with a Grid older
  than 1.3.0 a second answer can take the place of an honest copy that did not come back (see
  "Integrity");
- the compile of a task's own module is priced and its memory counted, not capped, and the
  bounds and the prices are fitted to measured shapes, not a proof about every module (see
  "Modules a task brings");
- three file calls take far longer on Windows than their price (`path_filestat_get`,
  `path_open`, `fd_filestat_get`), so a program that loops on them reaches the node's backstop
  there before its fuel: no verdict, `NoResult` (`wall_clock`), never a wrong one (see
  "Determinism");
- a trap is an explicit check in a host under hardware stack protection and a hardware fault
  elsewhere; verdicts, outputs and fuel are the same in both modes, but how deep a recursion
  goes before the engine's stack ends depends on the mode and the processor (see "What a program
  can reach");
- the scale runs on the fleet (a million tasks) were measured before 0.13.0, and are to be
  repeated on the 1.0.0 builds.

## Activities

| Activity | Where | Role |
|---|---|---|
| `Verify.Split` | host | taskset (JSON lines) + options → batches, grouped by runtime, limits clamped to the ceilings; the batches are grouped into packages, each stored as a blob with a ticket of its own, and what the activity returns is the references. The taskset is read a line at a time, its tasks wait in working files of the server's own, and the packages are stored one at a time. A taskset with a line the schema does not allow (an unknown field, a field given twice, a value of another type, a number outside its range), a source name that is not portable, an unknown runtime id, a task whose text is over 8 MiB or more tasks than a taskset may have is refused, by name |
| `Verify.ExecutePackage` | node | the primitive, and the one way work reaches a node. The node checks the package before it runs any of it and refuses it whole when it cannot run it (another controller version, a package or batch larger than a node takes, a runtime it lacks or has other bytes of). It downloads the package blob and runs its batches — one compiled runtime module per batch, the tasks executed concurrently in isolated stores under fuel and memory limits clamped to the ceilings — writes the results to a file batch by batch and uploads the file as a blob; progress of the delivery and of the work; batch provenance |
| `Verify.Repairs` | host | the task packages of a fan-out + what it returned → the tasks that have no verdict yet (no accepted answer, or only a fault of the node that ran them), as task packages to send out once more |
| `Verify.Disputes` | host | the task packages of a fan-out + what it returned, and the same of the repair round when the script hands it over → the tasks whose executions disagree, in the first fan-out or in the repair round, as task packages for three copies (`Grid.Replicas(3)`) |
| `Verify.Settle` | host | everything the fan-outs gave out and returned (the first one, the repair round, and the dispute round when the script ran one), and the sampling rate they ran under when the script states it → the settled run as one ledger blob. Every result package is accepted or refused here, under its ticket; every task is ruled on here; the four documents are written here, once |
| `Verify.Collect` | host | the ledger → the verdict report (aggregates + one record per task; the run's identity, byte-identical regardless of placement as long as every node did its work and told the truth) |
| `Verify.CollectOutputs` | host | the ledger → the outputs (one JSON line per task: stdout, stderr, and the outcome and stdout of every case of a suite; byte-deterministic on the same condition). The sandbox log of a run, the corpus of a generation |
| `Verify.MakeCheckTasks` | host | the ledger of a settled generator run + a checker spec → check task packages, one check per passing candidate under the candidate's index |
| `Verify.Provenance` | host | the ledger → the provenance document (which tasks ran together, in which round, on which platform, with which wasmtime, controller and runtime module, and what each task cost: fuel, peak memory, wall time; never which machine, and nothing a machine could be measured by) |
| `Verify.SamplingRate` | host | options → `SamplingRate` clamped to 0..1, the share of the packages `Grid.Replicas` runs twice |
| `Verify.Integrity` | host | the ledger → the integrity document (the pairs of executions of the first fan-out and of the repair round and whether they agree on verdict, exit, outputs and cases; every dispute and how it ended; the counts of refused result packages, repairs and tasks without a result; the sampling that was asked for and, for every fan-out, the packages that came back with their answers and the packages nothing answered) |
| `Verify.Preflight` | host | counts, runtimes referenced, unknown runtimes, size estimate, ceiling notes, and whether `Verify.Split` would refuse the taskset — before anything runs. One pass over the taskset, nothing kept; the answer is bounded whatever the taskset (32 messages of a kind, 240 characters each, and a line for the rest) |
| `Verify.RuntimeDiagnostics` | host | wasmtime version (the native library is loaded to prove it), runtime target, per-runtime availability and hash — of the server's module; a node's module is exercised by `Verify.ExecutePackage` and described by the provenance |
| `Verify.Capabilities` | host | what a submitter can know before a run, as a document: the controller's version, the runtimes it carries, the defaults and ceilings, how fuel relates to time, the source-name rule, what a task's own module is held to, the caps on packages and batches, the bounds of a taskset and of what a preflight answers (`taskset`: `max_line_bytes`, `max_tasks`, `max_sources`, `max_args`, `max_suite_cases`, `max_preflight_messages`, `max_preflight_messages_per_kind`, `max_preflight_message_length`, `max_preflight_unknown_runtimes`), and the pool as totals (nodes, concurrent sandboxes, reference tasks per second; no node named) |

## Scripts

`OutWit.Controller.AI.Verify.Scripts` bundles:

| Script | Shape | Returns |
|---|---|---|
| `VerifyRun(Blob taskset, VerifyOptions opts)` | Split → ForEach ⇒ ExecutePackage (a share of the packages twice) → Repairs → ForEach ⇒ ExecutePackage (the same share twice) → Disputes → ForEach ⇒ ExecutePackage (three copies) → Settle → Collect, CollectOutputs, Provenance, Integrity | `report`, `outputs`, `provenance`, `integrity` |
| `VerifyGenerate(Blob taskset, VerifyOptions opts)` | the same fan-outs; the programs are generators | `corpus`, `report`, `provenance`, `integrity` |
| `VerifyGenerateAndCheck(Blob generators, Blob checker, VerifyOptions opts)` | generators fan out once each → Repairs → Settle (a ledger of their own) → MakeCheckTasks → the checks fan out as in `VerifyRun` → Settle → Collect | `report` (the checks), `corpus` (the candidates), `provenance`, `integrity` |
| `VerifyPreflight(Blob taskset, VerifyOptions opts)` | one host activity | `result` |
| `VerifyRuntimeDiagnostics()` | two host activities | `result`, `capabilities` |

`VerifyOptions`: `DefaultLimits` (its wall time is accepted and is not a limit),
`BatchSize` (tasks per batch, 0 = 32, clamped to 4,096), `MaxDegree` (concurrent
sandboxes per node, 0 = 16), `SamplingRate` (the share of packages executed
twice for integrity, 0–1; 0 = none), `ReportFormat` (reserved).

The scripts as they are:

```
Job:VerifyRun(Blob:taskset, VerifyOptions:opts)
{
    VerifyTaskPackageCollection:packages = Verify.Split(taskset, opts);

    Double:rate = Verify.SamplingRate(opts);
    ProcessingOptions:checked = Grid.Replicas(2, rate);

    VerifyResultPackageCollection:results = Grid.ForEach(package in packages, checked)
        => Verify.ExecutePackage(package);

    ~ A task without a verdict (no answer the server accepts, or a fault of the node that ran it) goes out once more, under the same sampling. ~
    VerifyTaskPackageCollection:repairs = Verify.Repairs(packages, results);
    VerifyResultPackageCollection:repaired = Grid.ForEach(package in repairs, checked)
        => Verify.ExecutePackage(package);

    ~ A task whose executions disagree runs three more times, on different machines. ~
    VerifyTaskPackageCollection:disputes = Verify.Disputes(packages, results, repairs, repaired);
    ProcessingOptions:thrice = Grid.Replicas(3);
    VerifyResultPackageCollection:settled = Grid.ForEach(package in disputes, thrice)
        => Verify.ExecutePackage(package);

    Blob:ledger = Verify.Settle(packages, results, repairs, repaired, disputes, settled, rate);

    Blob:report = Verify.Collect(ledger, opts);
    Blob:outputs = Verify.CollectOutputs(ledger, opts);
    Blob:provenance = Verify.Provenance(ledger);
    Blob:integrity = Verify.Integrity(ledger);
}
```

```
Job:VerifyGenerate(Blob:taskset, VerifyOptions:opts)
{
    VerifyTaskPackageCollection:packages = Verify.Split(taskset, opts);

    Double:rate = Verify.SamplingRate(opts);
    ProcessingOptions:checked = Grid.Replicas(2, rate);

    VerifyResultPackageCollection:results = Grid.ForEach(package in packages, checked)
        => Verify.ExecutePackage(package);

    ~ A task without a verdict (no answer the server accepts, or a fault of the node that ran it) goes out once more, under the same sampling. ~
    VerifyTaskPackageCollection:repairs = Verify.Repairs(packages, results);
    VerifyResultPackageCollection:repaired = Grid.ForEach(package in repairs, checked)
        => Verify.ExecutePackage(package);

    ~ A task whose executions disagree runs three more times, on different machines. ~
    VerifyTaskPackageCollection:disputes = Verify.Disputes(packages, results, repairs, repaired);
    ProcessingOptions:thrice = Grid.Replicas(3);
    VerifyResultPackageCollection:settled = Grid.ForEach(package in disputes, thrice)
        => Verify.ExecutePackage(package);

    Blob:ledger = Verify.Settle(packages, results, repairs, repaired, disputes, settled, rate);

    Blob:corpus = Verify.CollectOutputs(ledger, opts);
    Blob:report = Verify.Collect(ledger, opts);
    Blob:provenance = Verify.Provenance(ledger);
    Blob:integrity = Verify.Integrity(ledger);
}
```

```
Job:VerifyGenerateAndCheck(Blob:generators, Blob:checker, VerifyOptions:opts)
{
    VerifyTaskPackageCollection:generatorPackages = Verify.Split(generators, opts);

    VerifyResultPackageCollection:candidates = Grid.ForEach(package in generatorPackages)
        => Verify.ExecutePackage(package);

    ~ A generator without a verdict goes out once more. The generators run once each: nothing of theirs is compared. ~
    VerifyTaskPackageCollection:generatorRepairs = Verify.Repairs(generatorPackages, candidates);
    VerifyResultPackageCollection:generatorRepaired = Grid.ForEach(package in generatorRepairs)
        => Verify.ExecutePackage(package);

    Blob:generated = Verify.Settle(generatorPackages, candidates, generatorRepairs, generatorRepaired);

    VerifyTaskPackageCollection:checkPackages = Verify.MakeCheckTasks(generated, checker, opts);

    Double:rate = Verify.SamplingRate(opts);
    ProcessingOptions:checked = Grid.Replicas(2, rate);

    VerifyResultPackageCollection:verdicts = Grid.ForEach(package in checkPackages, checked)
        => Verify.ExecutePackage(package);

    ~ A check without a verdict goes out once more, under the same sampling. ~
    VerifyTaskPackageCollection:repairs = Verify.Repairs(checkPackages, verdicts);
    VerifyResultPackageCollection:repaired = Grid.ForEach(package in repairs, checked)
        => Verify.ExecutePackage(package);

    ~ A check whose executions disagree runs three more times, on different machines. ~
    VerifyTaskPackageCollection:disputes = Verify.Disputes(checkPackages, verdicts, repairs, repaired);
    ProcessingOptions:thrice = Grid.Replicas(3);
    VerifyResultPackageCollection:settled = Grid.ForEach(package in disputes, thrice)
        => Verify.ExecutePackage(package);

    Blob:ledger = Verify.Settle(checkPackages, verdicts, repairs, repaired, disputes, settled, rate);

    Blob:report = Verify.Collect(ledger, opts);
    Blob:corpus = Verify.CollectOutputs(generated, opts);
    Blob:provenance = Verify.Provenance(ledger);
    Blob:integrity = Verify.Integrity(ledger);
}
```

```
Job:VerifyPreflight(Blob:taskset, VerifyOptions:opts)
{
    VerifyPreflight:result = Verify.Preflight(taskset, opts);
}
```

```
Job:VerifyRuntimeDiagnostics()
{
    VerifyRuntimeDiagnostics:result = Verify.RuntimeDiagnostics();
    Blob:capabilities = Verify.Capabilities();
}
```

In `VerifyGenerateAndCheck` the generators run once each (not sampled, but
repaired), their run is settled into a ledger of its own, and the checks are
made from that ledger: one settled candidate a generator, the same one the
corpus holds.

## How tasks and results travel

Tasks and results travel as blobs, end to end. `Verify.Split`
groups the batches into packages and stores each package as a blob; the
collection `Grid.ForEach` places on nodes is a list of references (blob id,
hash, size, task count, ticket). A node is told which packages are its share, downloads
exactly those over the blob channel — in chunks, resumed after a break — and
returns each package's results as a blob of its own; the host reads the result
blobs when it settles the run. `Verify.ExecutePackage` is the
one way work reaches a node: the single-task activity `Verify.Execute` is
removed.

An activity's parameters and its return value are references: a blob's id, or a
package reference of about 230 bytes (a result's about 145). The one thing in a
message that still grows with a run is the list of those references, one a
package, because `Grid.ForEach` fans out over a collection. Nothing that grows
with the taskset is read whole into memory either: the server reads a taskset a
line at a time (a preflight keeps nothing; a split keeps the tasks in working
files about the size of the taskset until their packages are stored), makes the
repair and dispute rounds and the check tasks a package at a time, and writes
the four documents to files as they are made. It keeps a small record a task,
never a task's text, its outputs or a document.

Measured by the controller's load test (0.14.0; the server and three nodes in
one process on a development desktop): 100,000 tasks of 2 KiB each, a taskset of
202 MiB, a twentieth of the packages run twice. What was live on the managed
heap (read after a full collection, four times a second) rose by 2 MiB at the
most while the taskset was split and by 45 MiB over the whole run; the heap as
it stood, garbage included, by 20 MiB and 65 MiB; the working set was 356 MiB
at its highest. The run was 1,562 task packages and 1,641
result packages, about a megabyte of blobs each way. The largest message to a
node, its whole share of 547 packages, was 126,357 bytes; the largest single
request 231 bytes. The job's two lists of references were 360,822 and 237,945
bytes; every other variable a few words.

A package is one batch while a run is small, so the split between nodes stays
as fine as the batches. A large run is cut into about a thousand packages
when the caps below allow it,
which keeps the number of transfers bounded while the shares stay proportional
to the nodes' rates. A package blob is Brotli-compressed; a node checks its
hash before it reads the tasks, and the host checks a result blob's hash before
it reads the results.

The caps hold for every batch and every package:

- A batch holds 32 tasks by default, 4,096 at most. It is also cut where its
  tasks' text reaches 8 MiB (characters) or their output caps together reach
  64 MiB; a task counts for twice its stdout cap plus its stderr cap.
- A package holds at most 4,096 tasks, 8 MiB of task text and 256 MiB of output
  caps. At the default caps a task counts for 576 KiB, so a batch holds at most
  113 such tasks and a package 455: a run of more than about 455,000 tasks at
  the default caps is cut into more than a thousand packages (a million into
  about 2,200). Tasks with smaller output caps make larger packages.
- A single task whose text is over 8 MiB is refused by its index when the
  taskset is split.
- A node holds itself to all of them whatever the package says: limits are
  clamped to the ceilings on the node, and a package or a batch larger than a
  node takes is refused whole.

A node writes the results to a file batch by batch and uploads the package from
the file, so it holds the results of one batch in memory, never of a package.
The server reads a result package frame by frame, each within a bound.

Measured on the test fleet, before 0.13.0: the million small tasks of the scale run leave the
host as 1,116 package blobs, 3 MB in all (the tasks are short and alike; real
tasks shrink less; at the default output caps the same run is now about 2,200
packages). In a 100,000-task run a node whose link from the server
was limited to 8 kbit/s received its 77 packages and finished its share before
the run's last node did.

## Documents — authoring without the SDK

Every input and output is a JSON document with a published schema in
[`docs/`](docs/), so a pipeline in any language can author tasksets and read
results; the SDK only moves blobs and submits scripts.

| Document | Schema | Direction |
|---|---|---|
| taskset (JSON lines, one task per line) | [`verify-taskset.schema.json`](docs/verify-taskset.schema.json) | in |
| checker spec (one object) | [`verify-checker.schema.json`](docs/verify-checker.schema.json) | in |
| verdict report (`verify-report/3`) | [`verify-report.schema.json`](docs/verify-report.schema.json) | out |
| outputs of a run, corpus of a generation (JSON lines) | [`verify-corpus.schema.json`](docs/verify-corpus.schema.json) | out |
| capabilities (`verify-capabilities/3`) | [`verify-capabilities.schema.json`](docs/verify-capabilities.schema.json) | out |
| provenance (`verify-provenance/2`) | [`verify-provenance.schema.json`](docs/verify-provenance.schema.json) | out |
| integrity (`verify-integrity/3`) | [`verify-integrity.schema.json`](docs/verify-integrity.schema.json) | out |

A minimal taskset — one code-vs-tests task and one plain run:

```json
{"index":0,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"a=int(input());b=int(input());print(a+b)"},"suite":[{"stdin":"2\n3\n","expected_stdout":"5\n","expected_exit":0}]}
{"index":1,"runtime":"quickjs-0.15.1","entry":"m.js","sources":{"m.js":"console.log(6*7)"}}
```

The controller accepts exactly what the taskset schema allows, and refuses a
line by the name of what is wrong with it: a field the schema does not have, at
any level (`unknown field 'suite[0].expected_stdoutt'`: a misspelt expectation
can never be dropped and turn a test into a pass); a field or a source name
given twice; a value of another type (`'suite' must be an array of cases`; a
`null` is another type); a number outside its range (`'limits.fuel' must be 1
or more`); a NUL character in a source, an argument or the entry (a program is
handed to its interpreter as a C string; in `stdin` and `expected_stdout` a NUL
is data); text that is not valid Unicode; more than 1,024 sources, arguments or
cases; a line over 64 MiB. A taskset has at most 4,194,304 tasks. A checker
spec is held to its schema the same way. `limits.wall_ms` is deprecated: it is
accepted and is not a limit.

Source names must be portable, for every runtime, and a taskset with another
name is refused where it is split and reported by the preflight, by name. The
rule: the characters `A-Z a-z 0-9 . _ - +`, folders separated by `/`; at most
128 bytes in all and 8 segments; no empty segment, no `.` or `..`, no segment
that starts with `-` or ends with `.`, none that Windows reserves for a device
(`CON`, `NUL`, `COM1`, …); no two names that differ only in case; no name that
is a file and a folder of another. Names with characters outside ASCII are
refused. As a regular expression:
`^(?!-)[A-Za-z0-9._+-]+(/(?!-)[A-Za-z0-9._+-]+){0,7}$`. An unknown runtime id
refuses the taskset in the same way.

Verdicts: `Pass`, `Fail` (ran, wrong answer or missing signal), `CompileError`,
`RuntimeError` (crashed, or exited non-zero where 0 was expected — whatever the status,
126 and more included, as `exit(200)` is on any system; a crash the program did
not explain is named on stderr in fixed words, `wasm trap: …`), `Timeout` (out of
fuel, and nothing else: the same on every node; the sandbox adds no words to stderr),
`MemoryExceeded` (the program's allocator was refused), `OutputExceeded` (the write
that took stdout past its cap ended the run), `NoResult` (no node gave a verdict, also
when the task was sent out once more; a `reason` says why: `wall_clock`, `node_fault`
or `no_answer`), `Disputed` (the executions disagreed and three more did not settle
it). `RuntimeUnavailable` is no longer produced; the name stays in the enum so that
old documents can be read. A node that lacks a runtime, or whose copy fails its
SHA-256 pin, refuses the whole package and the platform gives it to another node.
A suite case passes when the program ended by itself (it returned or called
exit) with its `expected_exit` (whatever it is, 200 as well as 1) and, if
given, its exact `expected_stdout`. A crash is not an exit: `expected_exit: -1`
does not pass on one. A case that is met passes whatever the program wrote to
stderr: words like "out of memory" there no longer turn a met case into
`MemoryExceeded`. The cases of a suite share the task's stdout cap; a case that
prints after the cap is spent keeps nothing and is `OutputExceeded`. A task runs
once a case, each run under the task's fuel and memory limits, and a suite has
at most 1,024 cases: the most work one task can ask of a node is its limits
times its cases.

What the documents say of a run that did not go well: a `NoResult` or a
`Disputed` task is in the report (never left out, never a pass; `pass_rate` is
pass / total) and has an outputs line with empty stdout and stderr. The
report's summary counts the result packages the server refused
(`rejected_results`) and the disputes (`disputes`, `disputes_resolved`), and
states the sampling: the rate that was asked for (`sampling_rate`; `null` when
the script did not say), whether it took place (`sampling`) and the task
packages no accepted result came back for (`unanswered_packages`). The
provenance names the round of every batch (`main`, `repair`, `dispute`) and
carries a `fault` for a task a node could not run. It has no degree and no wall
time of a batch: with the memory cap and the degree the lab's to choose, a
node's cores and memory could be read off them. The integrity document is
described under Integrity below.

Generate-and-check: the generators are an ordinary taskset whose outputs are
the candidates; the checker spec is one task line plus `expected_stdout` /
`expected_exit`. Every candidate that passed generation with output becomes
one run of the checker with the candidate on stdin, under the candidate's
index, so the check report lines up with the generators; candidates that
produced nothing usable are skipped (and still appear in the corpus). The
candidates are read from the generator run's ledger: what is checked is what
the corpus holds. The recommended checker contract: print the verdict and exit
0, so that a crash of the checker itself stays distinguishable as a
`RuntimeError`.

## Integrity

Anonymous nodes are trusted by checking, not by assumption. `SamplingRate` runs
a share of the packages twice, in the same `Grid.ForEach` call as the rest. The
platform picks the packages at random and keeps the two executions on different
machines, so a node cannot tell whether its package is being checked. The
integrity document pairs the two executions of each task and says whether they
agree on verdict, exit code, outputs and per-case outcomes —
exactly the fields an honest execution reproduces anywhere. With a single machine
available nothing runs twice, and the documents say so: the sampling is `short`.
The mismatch rate is over the compared pairs.

What the server does with what nodes return:

- **Tickets.** Every task package has a ticket: 16 random bytes (32 hex
  characters), made when the package is, sent to the node with it and returned
  in the result. Tickets are never written to a document.
- **Intake.** The server accepts a result package only under a ticket of that
  fan-out, when the blob is the bytes its reference names, was made by the
  server's own controller version, engine and runtime bytes, and holds exactly
  the tasks of the package it answers (none missing, none foreign, none twice).
  The package is walked before it is decoded: a frame with a length that leaves
  the frame, a count outside its bound (1,024 case records a result) or text
  that is not valid Unicode is one that cannot be read. And a result is held to
  its task: fuel within the budget of the task's runs, peak memory within its
  cap, no time below zero, stdout within its cap, the case records of its
  suite and no other, a verdict that is the one those records give.
  Anything else is dropped and counted (`rejected_results`), by reason:
  `unknown_ticket`, `duplicate`, `unreadable`, `version`, `content`, `surplus`.
  A dropped package fails that package and not the run, whatever it was that
  failed while it was read.
- **One controller version a job.** A package names the version that made it
  (`RequiredControllerVersion`), and a node on another version refuses it
  before doing anything.
- **The repair round.** A task with no verdict from the first fan-out (no
  accepted answer, or only a fault of the node that ran it) is sent out once
  more. If it still has none it is `NoResult` with a `reason`: `wall_clock`,
  `node_fault` or `no_answer`. The round runs under the sampling of the first
  fan-out (the same share of its packages twice, on different machines), and
  its executions are ruled on the same way: two that agree are accepted, two
  that disagree are a dispute. The platform chooses the packages that run
  twice and the server does not learn which, so a package that came back
  without any answer is repaired at the rate that was asked for, not because
  it is known to have been one of them. A fan-out that comes back short does
  not turn into a round nobody checks.
- **What was asked for, and what came back.** The report and the integrity
  document state the sampling rate and say whether the sampling took place:
  `as_asked` when as many packages came back with two accepted results as the
  rate asks for (the share rounded up to a whole package, one at least), in the
  first fan-out and in the repair round; `short` when fewer did (answers not
  returned or refused, copies the platform could not place, a pool of one
  machine); `none` at a rate of 0; `not_stated` when the script does not hand
  the rate to `Verify.Settle`. `unanswered_packages` counts the task packages
  no accepted result came back for, every fan-out together. A short fan-out is
  said, not punished: the verdicts are settled by the same rules either way.
- **No choosing.** The server never chooses between two executions. Executions
  that agree are accepted. Executions that disagree are a dispute: the task
  runs three more times on different machines (`Grid.Replicas(3)`), and the
  accepted result is the one returned by the most of those three, at least two,
  with no tie. Otherwise the task is `Disputed`: out of the pass counts (it is
  not a pass) and of the products (its outputs line has empty stdout and
  stderr).
- **One machine.** A task that was not sampled rests on one machine; the
  integrity document counts those (`single_execution`).

The integrity document (`verify-integrity/3`) has the summary (`tasks`,
`sampled`, `compared`, `mismatches`, `mismatch_rate`, `agreed`,
`single_execution`, `disputes`, `disputes_resolved`, `disputed`, `repairs`,
`no_result`, `rejected_results`, `rejected` with the six reason counts, and
`sampling_rate`, `sampling`, `unanswered_packages` as in the report), the
`fan_outs` (for each round: `packages` given out, `copies` a selected package
was asked for, `selected` packages asked for that many, `complete` packages
that came back with that many accepted results, `unanswered` packages), the
`pairs` (each with the `round` its two executions ran in, `main` or `repair`),
and the `disputes`: for each, `index`, `first_copies`, `copies`, `agreeing`
and an `outcome` (`resolved`, `no_majority`, `too_few_copies`,
`surplus_copies`).

The known limit: the server sees results, not machines. Reading answers as
machines rests on the platform holding a node to one result for each package it
was given; the controller cannot check that itself. Grid 1.3.0 and later hold a
node to it: of what a node's batch returns they keep one value at most for each
return variable its tasks asked for. By the controller alone,
whatever a node returns: a node answers only for packages it was given; in the
first fan-out, and in a repair round under its sampling, more answers than the
two asked for send the package's tasks to a dispute; in the dispute round, and
in a repair round that asked for each package once, a package with more answers
than were asked for has none of them taken; so a node does not win a dispute in which all
three copies came back. Where the platform holds a node to one result a
package, a node that answers one package twice has left another short of its
answer, and in the dispute round an answer counts towards "two machines" only
as far as it cannot be one machine's second: of the answers for the leading
result, as many are left out as the other packages of the round are short of,
and if the result no longer leads by two the task stays `Disputed`
(`surplus_copies`). With one lying node among the copies, a result accepted
from a dispute was then returned by an honest machine. Where a node is not held
to it (a Grid older than 1.3.0), a second answer can take the place of a copy that did not come back, and
the controller cannot tell that from two machines outvoting a third. One lying
node can keep a dispute from being resolved, and so can answers that fail to
come back (in a pool of fewer than three machines a round with more than one
package in dispute settles nothing). Two lying nodes that hold two of a task's
three copies are the majority, and win.

## Determinism

The verdict report is the run's identity: it carries verdicts, exit codes and
per-case outcomes — the fields two honest executions agree on wherever they
ran — and is byte-identical for a taskset and its options regardless of which
nodes ran which packages, as long as every node did its work and told the truth
(proven on the test fleet across three placements, before 0.13.0). Its summary
also states the sampling rate and whether the sampling took place;
in a pool of one machine nothing runs twice, and the report of a run that asked
for sampling says `short` there. `Timeout` is out of fuel,
which is the same on every node; a run a node's clock ended has no verdict.
Fuel is the program's own since 0.12.0: the budget is given once the module is instantiated,
so neither the budget nor the fuel reported carries the copy of the module's
data segments, which wasmtime charges a unit a byte for where it copies them and
not where it maps a copy-on-write image (Linux). That copy was the fixed step
between Linux and the other two systems that earlier versions showed for Ruby and
Lua (a Lua hello cost 569,680 units on Linux and 602,449 on Windows and macOS;
32,769 of that was the copy): the hello of every runtime is now one number on
Windows, Linux and macOS, pinned by the tests. That holds for the CPython tasks
that read their library from files (`python-3.14.6` and its sympy flavour) as
well: up to 0.12.0 their fuel drifted between hosts by a few hundredths of a
percent (on the build server a hello cost 1,153 units and the import of sympy
968,240 units less on Linux than on Windows and macOS), because the
interpreter's imports read the host's file records (times, device, file
number); since 0.13.0 those records say nothing of the host, and the build
server has measured the same figure on the three systems since.
The tests also list a CPython hello's calls into the host in
order, and the listings of the three systems are equal call by call. Fuel is
reproducible per host; the peak linear memory of a CPython task can differ by
one 64 KiB page between hosts; wall time is hardware. All three are metrics and live per task in the provenance
document.

Since 0.13.0 a call into the host costs fuel in proportion to the bytes it
moves: one unit a byte read from a file, written to stdout or stderr, filled by
`random_get`, or of a path that is looked up (the rate the engine charges for a
guest's own bulk memory copy). So every fuel figure moved: a task's fuel grew
by the bytes its runtime reads at start, and figures quoted before 0.13.0 are
lower by that.

Since 0.17.0 a call into the host also costs a fixed 1,000 units, whoever
answers it; a wait (`poll_oneoff`) costs the bytes of its records, 48 a
subscription and 32 an event; and the arguments and the environment cost their
bytes each time a program copies them. So every fuel figure moved once more, by
a thousand units for every call the program's runtime makes (a CPython hello
makes 118, the import of sympy 7,814), and the two runtimes whose module the
engine compiles on the node also pay for their smallest functions, which the
price of compiling counts as 24 bytes each at the least. The hellos now, on
Windows x64, with the figure of 0.13.0 to 0.16.0 in brackets: `python-3.14.6`
122,481,638 (122,363,490); `python-3.14.6-sympy-1.14.0` with `import sympy`
3,213,154,830 (3,205,340,541); `python-3.14.6-numpy-2.5.4` with `import numpy`
24,941,299 (24,920,091); `quickjs-0.15.1` 2,367,084 (2,359,037); `ruby-3.4.1`
153,965,549 (153,868,237); `lua-5.4.8` 588,454 (571,435);
`typescript-sucrase-3.35.1` 333,420,186 (333,353,693); a C hello 8,628,668
(8,314,372) and the 224-byte raw WASI hello 482,292 (458,764). The last two
reported 710 and 6 before 0.13.0: the price of compiling the module is in the
task's fuel, see below.

The thousand units are half a microsecond at the reference rate, about what the
host's own part of most calls takes. Three file calls take far longer on
Windows (`path_filestat_get`, `path_open` and `fd_filestat_get`: 45 to 190 µs
each where measured), so a program that loops on them burns some 7 million
units a second there, and the node's backstop ends it before its fuel does: no
verdict (`NoResult` with reason `wall_clock` if no node does better), never a
wrong one.

The clock is the run's own (it starts at a pinned instant and moves
only when the program sleeps, and every clock reports a resolution of 1 ns) and
the random source is seeded from the task, so a program sees the same world on
every node; a folder cannot be listed, so the host's order of a listing is out
of sight. Every seed has its own random stream (seeds 2k and 2k+1 used to share
one): seed 0, the default, keeps the stream it always had, and every other
seed's stream changed in 0.13.0.

The file system is the same on every node too. A guest reaches a folder through
a table of its exact names: a lookup under another spelling (`Util.h` for
`util.h`, `LIB.Util` for `lib/util.lua`, a Windows short name, an alternate
data stream, a backslash) finds nothing on any node. In a runtime's folder only
the pinned files exist (for Python and TypeScript the pinned layers, for Ruby
and the C sysroot the pinned tree), and nothing can be created, removed or
renamed. A file's record says nothing of the host: its three times read as
zero, its device as zero, and its file number follows from the guest path
alone; a folder has size zero. So `os.stat(...).st_atime_ns`, `st_ino` and C's
`__TIMESTAMP__` (`Thu Jan  1 00:00:00 1970`) are the same everywhere. Before
0.13.0 the times and file numbers of the runtime's own files were the host's,
and a program that printed them disagreed between nodes.

## What a program can reach

Every run is one WebAssembly instance in a store of its own, created for the
run and dropped after it. What the store gives a program, and what happens at
each edge:

| | What a program gets | At the edge |
|---|---|---|
| Processor | fuel, one unit an instruction, 1,000 a call into the host and one a byte the call moves: 20e9 by default (about ten seconds of a plain busy loop on the slowest node the fleet has shown, at 2,000 million units a second), 240e9 at most (two minutes of such a loop) | `Timeout`: out of fuel, and nothing else |
| Wall time | no limit of the task's: `limits.wall_ms` is still accepted and is not a limit (the preflight says so with one message). A node keeps a backstop of its own, 10 s plus the time the fuel budget lasts at 500 million units a second: 50 s for the default budget, 490 s for the largest | no verdict: a fault of the node. The task is sent out once more, and is `NoResult` with reason `wall_clock` if no node does better |
| Memory | one linear memory of 32-bit addresses: 256 MiB by default, 2 GiB at most | the allocator refuses inside the program: `MemoryExceeded`; an access outside the memory is a crash: `RuntimeError` |
| Call stack | 8 MiB of the engine's native stack, as on Linux (the engine's default is 512 KiB), on threads made to hold it | `RuntimeError`, `wasm trap: call stack exhausted`; the interpreters stop a deep recursion themselves before that. How deep a recursion of a program's own goes before the stack ends depends on the node: see the next row |
| Traps | a trap (an unreachable, an access outside the memory, a division by zero, a stack overflow) is an explicit check in a host whose process runs under hardware-enforced stack protection (CET shadow stacks: a .NET 9+ executable on a Windows machine that enforces them), and a hardware fault the engine catches in any other | the host stays up either way: a process under stack protection is not killed by a guest's trap. The verdicts, the outputs and the fuel are the same in both modes, with one stated exception: a frame of the engine's stack is larger where traps are explicit checks, so a recursion exhausts the stack sooner there (a small C function returned from 104,850 levels with hardware traps and from 87,375 with explicit checks on one machine; processor architectures differ as well). `VerifyTrapModeTests` runs the same programs in both modes and compares them, a recursion of 20,000 levels (it returns in both) and one of 1,000,000 (it overflows in both) among them; between those a deep recursion can end differently on two honest nodes, and a sampled task is then a dispute |
| Tables | 1,048,576 elements a table, eight tables | growth is refused |
| Output | stdout: 256 KiB by default, 8 MiB at most, for a task with all the cases of its suite. stderr: 64 KiB by default, 1 MiB at most. Both are capped at the write: a byte past the cap is never stored, in memory or on disk | the write that takes stdout past its cap ends the run: `OutputExceeded`. stderr past its cap is dropped and flagged (`stderr_truncated`), and the run goes on |
| Files | the runtime's own library, read-only; for Lua the task's own sources, read-only; a raw `wasi` module and a C/C++ program have none. Only the exact names of the folder's table exist | writing, creating, truncating, removing and renaming are refused; a lookup under another spelling finds nothing |
| Folder listing | none | "permission denied" |
| Network, processes, threads | none | the calls do not exist or fail |
| Clock | the run's own: it starts at one pinned instant and moves only when the program waits | the clock shows nothing of the host's time; a file's three times read as zero, and C's `__TIMESTAMP__` is the same on every node |
| Pointers handed to the host | checked against the guest's memory | a wild pointer to `random_get` or `clock_time_get` answers WASI's `EFAULT` (21); it used to fail the whole batch |
| Sleeping | returns at once and moves the run's clock by what was asked | an hour's sleep costs nothing, holds no worker, and the clock reads an hour later |
| Randomness | a stream seeded by the task | the same bytes on every node |
| Environment | empty, but for what pins the interpreter | |

A module is refused before it runs (`CompileError`) when it asks for what the
sandbox does not offer: a second linear memory, 64-bit memories or tables,
shared memory, the GC heap, stack switching, exception handling, an import
the sandbox does not provide. Four system calls answer with an error and do
nothing, each for a stated reason (the comments in the wasm host's `WasmHostLinker`
name the engine advisories): renumbering a descriptor, listing a folder, and
setting a file's times by descriptor or by path.

These are tests, not intentions: the adversarial suite and the raw-WASI suite
run a hostile program or module against every row, and the node-protection
suite (`VerifyNodeProtectionTests`) runs what a hostile task could do to the
node: a flood of output, a module that is costly to compile, a wild pointer, a
run the clock ends, a batch that asks for more than the ceilings. The same in
plain words, for someone who lends a computer to the network:
[docs/safety.md](docs/safety.md).

### Modules a task brings, and programs the compiler builds

A module of the `wasi` runtime, and a program `clang-22.1.0` builds from a
task's sources, is compiled on the node, and nothing can stop a compile once it
has started. The engine's compiler takes time with the square of a function's
locals times the depth of its loops: 6.9 KB took 1.5 s, 28 KB took 81 s and
1.5 GiB. So the work is bounded before it starts:

- Such a module is compiled by an engine that uses the calling worker's thread
  only (the engine's own compile threads are off for it), so compiling stays
  within the cores a batch counts.
- Before compiling, the module is validated and its shape is priced. The price
  comes out of the task's fuel budget, once a task and not once a case: 2,048
  units of fuel a byte of the module; plus 4 units for every unit of (locals of
  a function)² × (depth of its nested loops); plus 100 units for every unit of
  (targets of a branch table) × (blocks around it).
- The price also has what the compiler was measured to take for
  other shapes (modules of a few kilobytes were found that took it 30 s, or
  8 GiB, for the price of a hello): 4 units for every unit of
  (branches of a function)², for a function of more than 1,024 branches; 128
  units for every unit of (locals a function uses) × (its branches) past
  16,384; 4 units for every unit of (values a block passes) × (blocks around
  it), for a block of two values or more; 64 units a local past 256 in a
  function; and a function is priced as 24 bytes at the least. A program a C
  compiler wrote pays for its bytes and for its smallest functions, and next to
  nothing else.
- A task whose budget does not hold the price is `Timeout`, and nothing is
  compiled.
- Blocks nested deeper than 2,048 in one function are refused: `CompileError`,
  with `a function of the module nests blocks deeper than 2048` on stderr. The
  shipped interpreters nest 115 to 781 deep.
- A module is also refused, with a `CompileError` that names the
  bound, when a function type has more than 128 parameters or more than 8
  results, a block takes and gives more than 8 values, a function has more than
  16,384 locals or a body larger than 128 KiB, or the module has more than
  32,768 functions or its functions declare more than 4,194,304 locals
  together. The interpreters the controller ships have at most 28 parameters,
  one result and 188 locals in a function; QuickJS has 1,522 functions and a
  largest body of 78 KB.
- A module with a start function is refused (`the module has a start function,
  which a task's module may not have: a program starts at _start`): what the
  engine runs while it makes a module ready is in no budget.
- A raw module may be at most 8 MiB.
- While it compiles, the engine takes memory that is in no task's memory cap:
  6 KiB a byte of the function it works on, what it keeps of every function
  until the module is done, which grows with that function's calls, loads and
  stores, loops, branches, float and vector instructions and instructions that
  may trap, and the code it makes for every function type and every function
  that leaves the module. It is estimated from the
  shape, and a module estimated over 1,536 MiB is refused (`compiling the
  module may take more than 1536 MiB of memory`); compiles that may take 64 MiB
  or more share 1,536 MiB between them in a node's process, each waiting while
  the others leave it no room, so that the largest runs alone and several of
  ordinary programs run side by side; the degree counts those 1,536 MiB (see
  "Node behaviour").

What remains: the bounds, the prices and the estimate of a compile's memory hold
for the shapes that were measured, and are not a proof about every module the
engine accepts; the compile itself is not capped; a compile, once
started, cannot be interrupted; one expensive compile runs at a time in a
node's process. Some code compiles far more slowly than its price says: calls
through a table, loops, reads of a table, traps, functions of many parameters
that leave the module, and loads and stores on a host with hardware stack
protection (where every trap is an explicit check). Within every bound
and the default budget, a module of many such functions can hold one core of a
node for minutes while it compiles, and that compile cannot be stopped. No
verdict is wrong because of it, and its memory is bounded. A non-optimising compiler
tier was evaluated and is not used: the engine's baseline compiler (Winch)
needs signal-based traps, which a Windows host with hardware stack protection
cannot have.

## Node behaviour

A worker is often a desktop. A batch runs on dedicated threads below normal
priority (Windows honours it; Linux ignores thread priorities, where the
degree cap is the protection) at `min(cores, usable RAM / per-task memory cap,
MaxDegree)` — 16 by default. The usable RAM is three quarters of the memory
that is free when the batch starts, and for C/C++ tasks the compiler's 1 GiB a
step counts as the per-task cap when it is the larger. For a batch whose
modules the engine compiles on the node (`wasi` and C/C++) what that work may
take is counted as well: 64 MiB a task, and, taken off the usable RAM once,
the 1,536 MiB the larger compiles share and the 256 MiB of
compiled code the node keeps for the tasks to come. Compiling a task's own
module or a program built from C/C++ takes the worker's thread only; the first
use of a runtime in a process compiles that runtime's own module (the
controller's, pinned) on the engine's threads, once.

The compiler has a budget of its own: 100,000 million units of fuel for all the
steps of a build and 1 GiB of memory a step, and its product (an object file,
the program) is capped at 64 MiB at the write. All that a build writes, its
objects and its program together, is capped at 128 MiB, at the write as well:
a build never holds more than that on a node's disk. Its objects and the
program are files in the client's own folder and are deleted after the task
ends.

A program's output never reaches the disk. What a run needs on disk — the file
a run's input is given through, a Lua or C/C++ task's sources, the compiler's
products, all deleted in the background soon after the run or the task ends (off
its own path: a real-time file scanner can hold a delete for a long time), and
a result package until it is uploaded
— lives in the client's own temp folder, and the task packages in the client's
blob cache: nothing of a task is written anywhere else on the machine.

A node checks a package before it runs any of it: the controller version that
made it, its size, and every batch's runtime. A package from another controller
version, one larger than a node takes, or one whose runtime the node lacks or
has other bytes of is refused whole, and the platform gives it to another node;
nothing is reported as a verdict. Limits are clamped to the ceilings on the
node, whatever the package says.

Nothing of a node's own words reaches a lab. A runtime that cannot be used, a
transfer that failed, a disk that is full are fixed words or reason codes
without a path, an account name or an exception's text; the detail goes to the
node's own log. (Before 0.13.0 a missing runtime file put the node's full local
path, with the account name, into a task's stderr.) The log itself cannot change
that: everything the controller logs on a node goes through one wrapper
(`VerifyLoggerGuarded`) that lets one thread at a time into the host's logger
and keeps an exception of the logger's own to itself. A line the logger refused
is lost; the package it was about completes, or fails with the fixed words.
(Cloud clients up to 2.2.5 gave a controller an in-memory list without a lock
as its logger; under a share of several packages it threw, and when it threw
inside the engine's own catch block its exception was what the server was told.
The engine's adapter base logs through the logger its constructor was given, so
every adapter hands it the guarded one.) The program's own stdout
and stderr are the program's and stay. Every accepted batch is in the
provenance with its round, runtime target, wasmtime and controller versions,
runtime and module hash, and what each task cost; the degree and the batch's
wall time are not in it, and do not leave the node.

A node starts every package of its share together. The package blobs download
ahead of the work, four at a time, so the share is on the node's disk early and
the rest of the run needs the link only for progress and results; one package
executes at a time, since a batch already takes the node's cores. A download or
an upload that fails is tried again for about five minutes before the package
fails, on top of the blob channel's own retries. The first transfer of a node
goes alone and the rest wait for it (the channel is connected by its first
user), and retries are spread at random so that transfers that failed together
do not come back together. Every attempt of a transfer has a time to finish in:
two minutes plus the time its bytes take at 32 KiB a second. One that does not
finish counts as failed and no longer blocks the node's other transfers.

A run is split once and sent once: each node gets a share by its benchmark
rate, a copy of a sampled package goes to another machine, and nothing is
handed out again while the run goes well (the repair round and the dispute
round are empty then). So a run ends when its slowest share
ends. A node that is slower than its benchmark said, or busy with its owner's
work, holds the run until it finishes; a node that goes silent loses its share
after the silence window (90 s by default), and the whole share is sent to
another node and done again from the start. With a benchmark that represents
the work and machines that are really free, the difference is small; on a
fleet of desktops it is the price of sending everything once.

The node reports its share, not one batch: first the delivery (`receiving
tasks: 12/215 packages, 3.1/14.8 MB; 0/220160 tasks done`), then the work
(`51200/220160 tasks`), about twice a second through the host's activity
progress sink. The fraction is tasks done out of the tasks of the node's share.

The node benchmark (`verify-task@mixed-v2`) is the throughput of a fixed
reference batch — one CPU-bound Python program and one QuickJS program, four
per worker thread, with their expected outputs — as tasks per second of the
median run at the node's degree; a node that computes wrong cannot score, a
node without runtimes, or with one that fails its pin, reports the default
(unranked) score: it would refuse the packages it was given. `Grid.ForEach`
allocates packages with the engine's `Balanced` strategy (pre-partitioned by
rate), which the package work estimate (task count) matches.

## Runtimes

Language runtimes are WebAssembly modules, platform-independent and pinned by
SHA-256 in the controller's catalog. A task names its runtime; a batch shares
one compiled module.

| Runtime id | What runs | Entry |
|---|---|---|
| `python-3.14.6` | CPython 3.14.6 (WASI build), pure Python with the standard library | the source, inline |
| `python-3.14.6-sympy-1.14.0` | the same interpreter with sympy 1.14.0 and mpmath 1.4.1 on `PYTHONPATH` | the source, inline |
| `python-3.14.6-numpy-2.5.4` | CPython 3.14.6 with numpy 2.5.4 built in, both built for WASI by this repository's toolchain and started from a snapshot with numpy imported; numpy's default integer is 32-bit (`dtype=np.int64` for 64-bit sums), no BLAS, threads or SIMD | the source, inline |
| `quickjs-0.15.1` | QuickJS-NG 0.15.1, JavaScript, built for WASI by this repository's toolchain | the source, inline |
| `typescript-sucrase-3.35.1` | TypeScript on the same QuickJS: sucrase 3.35.1 strips the types, the JavaScript runs as a module; no type check | the source, inline |
| `ruby-3.4.1` | Ruby 3.4.1 (the ruby.wasm 2.10.1 build) with the standard library and the bundled gems base64, csv, matrix and prime | the source, inline |
| `lua-5.4.8` | Lua 5.4.8, the stock interpreter with its standard library, built for WASI by this repository's toolchain | the sources as files; the entry is the script |
| `clang-22.1.0` | C17 and C++20 without exceptions, compiled and linked on the node by clang 22.1.0 for WASI with wasi-libc and libc++; the program runs as a raw WASI module | the sources as files; every `.c`, `.cc`, `.cpp`, `.cxx` is compiled, headers sit beside them; the entry names the program |
| `wasi` | the task's own WASI command module: Rust, C/C++, Zig, TinyGo, anything that targets wasm32-wasi (`_start`, WASI preview 1) | the module's bytes, base64; its name is `argv[0]` |

The Python library code is bytecode layers, one zip each, that CPython's
zipimport loads straight from `sys.path`: `python314.zip` (the standard
library) and `sympy-1.14.0.zip` (sympy + mpmath). Each layer is compiled by the
pinned interpreter itself as unchecked-hash `.pyc` (no source parsing, no
source stat at import) and pinned by SHA-256 in the catalog next to the
module, so every node runs byte-identical library code and a hello costs a
quarter less fuel than with a source tree, a stdlib import set a tenth. The
prefix is pinned (`PYTHONHOME=/`), nothing lists a directory at start-up.
Library frames show no source line in tracebacks; the sandbox compares
outputs, not tracebacks. sympy's `external/gmpy.py` is patched before
compiling (the C long size comes from a constant: the WASI build has no
`_ctypes`); the patch is declared in the layer's NOTICE.

numpy (since 0.11.0) is a runtime of its own, `python-3.14.6-numpy-2.5.4`: the same CPython
3.14.6 and numpy 2.5.4, both built from pinned sources by the asset tool's toolchain
(`toolchain/python-numpy`: CPython's own WASI driver with wasi-sdk 24; numpy's extension
modules linked into the interpreter as builtins, since WASI loads no shared library; the
standard library and numpy's Python package as bytecode layers compiled by the module itself;
two builds give the same bytes). The module is a start-up snapshot: the interpreter initialized
and numpy imported, written into the module by wizer of the pinned wasmtime 48.0.8 at build
time, so a task starts where a plain start would be after its imports. The sandbox runs it as
any module (`_start`). What a task sees is its own: `sys.argv`, the clock at the sandbox's
pinned start, and every source of randomness that drew entropy while the snapshot was made
(Python's `random`, numpy's legacy global `RandomState`, `tempfile`'s names) seeded again from
the task's stream, so unseeded random numbers follow the task's seed as in `python-3.14.6` (the
numbers differ from a plain start's, the rule does not). The snapshot holds `os.environ`,
`sys.path` and the directories of its two layers, so the module and the layers are pinned and
delivered together (their own data asset, `verify-python-numpy.zip`, next to `wasm/`), and a
run under another environment or file system is refused (exit 2, the reason on stderr). The
module before its snapshot travels beside it as `python-plain.wasm`, for debugging; the catalog
names the snapshot. A hello costs 25 M fuel (`python-3.14.6`: 122 M), `import numpy` nothing more
(a plain start: 0.8 G), a 300×300 `linalg.solve` about 0.24 G, a sort of a million integers 0.56 G.

What a numpy author should know about the target, also in the capabilities document's `notes`.
It is 32-bit: numpy's default integer is `int32`, so `np.arange(1_000_000).sum()` wraps to
1783293664; pass `dtype=np.int64` where a total can pass 2^31. There is no BLAS (numpy's own
`lapack_lite`, correct and slower), no threads (`threading` cannot start one) and no SIMD.
`np.longdouble` is IEEE binary128 in software, not x86's 80-bit extended, so long-double
results differ from a desktop's. Floating-point arithmetic sets no flags in WebAssembly: `1.0 /
0` is `inf` without a warning, while numpy's own errors (a singular matrix's `LinAlgError`, an
integer division by zero under `errstate`) are raised. A C++ exception inside numpy would stop the
program (`RuntimeError`); numpy checks its arguments before its C++ code could throw. Nothing is
writable: `np.save` and `np.load` work on `io.BytesIO`. `numpy.testing` is there, numpy's own test
suite is not.

`OutWit.Controller.AI.Verify.Assets` (`verify-assets build <dir>`) rebuilds
the whole set of runtime files from pinned downloads, byte for byte, and prints
the hashes the catalog pins. Its `toolchain/archives.sh` packs the release
archives from that set and from the native libraries: the entries in a fixed
order with fixed times and modes, compressed by zlib at level 9, so the same
files give the same archive. What "byte for byte" is said of, and what checks it:

- **the files** of the runtime set: the catalog pins every module and layer by
  SHA-256 and the two library trees by a tree hash; the tests build the set on
  three platforms at every change, and a node checks the pins before it runs a
  task;
- **the four archives** `archives.sh` packs (`verify-wasm.zip` and one of the
  native library for each platform): the `AI Verify archives` workflow builds
  them again at every change of the asset tool, of a notices file or of a pin,
  and fails when one is not the archive the controller's project file pins by
  SHA-256 and size;
- **the numpy runtime's archive**: its workflow builds it twice in folders of
  different names, compares the two, and fails unless the result is the pinned
  archive;
- **the modules the toolchain builds** (`lua.wasm`, `qjs.wasm`, `sucrase.js`,
  the fixtures): built twice and compared with the committed copies, on a pull
  request and after a merge.

An archive's bytes also depend on the compressor: the pins hold for zlib
(1.2.12, 1.3 and 1.3.1 were seen to give the same bytes). With another
compressor in its place the archive would unpack to the same files and the job
would say that its hash is another.

Ruby loads its library from files, not from one archive, so the library is a
tree: 1,142 files, cut unchanged out of the release archive (the standard
library and the `lib` folders of four bundled gems; headers, the static
library and the other gems stay out). The catalog pins the tree with one hash
over the relative path, the length and the bytes of every file, and a node
verifies it before the first task: with a file changed, added or removed the
node refuses the package, and the platform gives it to another node. The tree
is the guest's whole file system, read only — a program can read nothing that
is not under the pin, and reaches a file only under the exact name it has. A program runs
inline without RubyGems (it would scan the file system at every start, and
there is nothing to install); the four gems' folders are on the load path, so
`require 'prime'` works like any other. Not in this build: `bigdecimal`,
sockets, processes (`system` and backticks do nothing). It is a 32-bit build:
an Integer above 2^30 is a bignum, so arithmetic on large totals costs several
times more than on a desktop Ruby. A hello costs 154 M fuel and takes 55 MiB.

QuickJS-NG is built here too (since 0.8.0), from its pinned sources with one patch
(`toolchain/quickjs-wasi.patch`). The project's own WASI build places a 64 KiB C stack right
after its data and switches the engine's stack limit off on WASI, so a deep recursion wrote
over the interpreter's data: a correct program 162 levels deep ended with exit 0 and no output,
and an endless one got a verdict that depended on what the sandbox had run before it. Our build
puts the C stack (1 MiB) below the data, where an overflow traps at address 0, and turns the
limit back on at 128 KiB: a recursion too deep is JavaScript's `RangeError`, which a program can
catch, at about 300 levels of plain recursion and about 100 through callbacks such as `map`,
before the engine's own native stack runs out, on every platform the build server runs. A
structure nested far deeper than that in native code (`JSON.stringify` of 2,000 levels) still
reaches the native stack: the run traps, `RuntimeError`. QuickJS's own tests are a gate of the
toolchain job (the five that fail are about Atomics without threads, os functions, workers and
the `qjs` global, as with the project's build), and the build is the same bytes on any host.

TypeScript runs on the JavaScript runtime's module (one compile serves both) with a read-only
root that holds sucrase's transform, bundled by the toolchain from the npm packages its lock
file pins (`toolchain/typescript`: esbuild 0.25.10, 206 KB, the licences of the seven bundled
packages beside it; the build server rebuilds it and compares the bytes). QuickJS loads it
first (`-I /sucrase.js`); a small bootstrap module holding the task's source as a JSON string
strips the types and evaluates the JavaScript as a module, so `import * as std from 'qjs:std'`,
top-level `await`, `scriptArgs`, stdin and the exit code behave as in a JavaScript task. Enums,
`const enum`, parameter properties, abstract classes, `#private` fields, `satisfies`, `as
const` and generics work; a runtime `namespace` does not (sucrase does not emit one) and there
is no type check. A source that does not parse is a `RuntimeError` whose stderr names the line
and column. A hello costs 0.33 G fuel, most of it loading the transpiler.

Lua is built here from lua.org's sources, unchanged, by the asset tool's toolchain
(`toolchain/build.sh`: wasi-sdk 34 and Binaryen 133, every download pinned, two builds
give the same bytes; the build server checks that the committed `modules/lua-5.4.8/lua.wasm`
is what the sources build to). Lua raises an error by a jump to the nearest protected call,
which WebAssembly can only do with its exception handling, and the sandbox keeps that off
for every module. So the jump is an asyncify unwind that stops at the protected call and
is never rewound (`toolchain/lua-asyncify.h`): the module imports WASI alone and holds no
exception instruction. Lua's own test suite passes as it does with exceptions (27 files of
30; three fail for reasons of WASI: no locale data, no temporary files, no `PATH`), and the
deepest unwind stays under half its 256 KiB buffer; both are gates of the toolchain job.
The cost is about 1.4 times the fuel of a build with exceptions (coroutines are faster);
a hello costs 0.6 M fuel and takes 1.3 MiB.
A Lua task's sources are written as files into a fresh folder in the node's working folder,
which is the guest's whole file system, read-only, and is removed when the task ends; the
entry runs as Lua's script, so `arg`, `...` and `require` of a neighbour or of `lib/x.lua`
work. A name must be the same file on every node: the names are held to the portable set
for every runtime (see Documents above), and a taskset with another name is refused where it
is split. The guest reaches the folder through a table of exactly those names, so
`require('LIB.Util')` does not find `lib/util.lua` on any node, whatever the node's own file
system would have matched.

C and C++ are compiled on the node (since 0.10.0) by clang 22.1.0 for WASI: LLVM as one
WebAssembly module (YoWASP's build, 75 MB) and the sysroot it reads at `/usr` (wasi-libc,
libc++, libc++abi, the compiler-rt builtins, clang's headers; 2,345 files pinned as a tree,
like Ruby's library), both cut unchanged from the pinned npm package. WASI starts no
processes, so the build is the clang driver's plan run step by step, each step its own
instance of the module: `clang -cc1` for every `.c` (C17) and every `.cc`, `.cpp`, `.cxx`
(C++20), then `wasm-ld`. Nothing is writable for the tools: a step writes its product to
stdout (the object, then the program module), the sandbox puts each object into a fresh
read-only folder, and the next step reads it there. A product is capped at 64 MiB at the
write, and everything a build writes together at 128 MiB; the objects and the program are
files in the client's own folder, deleted after the task ends. The command lines are fixed in the controller (`-O2`), the sources are written as
files as for Lua (the same names; headers sit beside the sources, and a header is found
under the exact name it has and under no other), and all the steps of a task share one
budget of their own, apart from the program's limits: 100 G fuel (fuel binds, so the same
sources build the same way on every node) and 1 GiB of memory a step. A 600 s wall backstop
on a build gives no verdict: a build the clock ended is a fault of the node, and the task is
sent out once more. A C hello builds in 0.2 G fuel; a C++ program of a couple of hundred
lines with the standard library in 10–20 G, about 5 s (both measured before 0.13.0; a build
now also pays a unit a byte the tools read and write). The program is then priced and
compiled like a module a task brings (above) and runs as a raw WASI module under the task's
limits, `argv[0]` the entry's name without its extension, with no file system at all. A
compile or link error, a build that runs out of its fuel, a product over its cap and a build
over its 128 MiB are the task's `CompileError`, with clang's diagnostics or the reason on stderr. A file's time reads
as zero in the sandbox, so `__TIMESTAMP__` is `Thu Jan  1 00:00:00 1970` on every node. C++ is C++ without exceptions:
the shipped libc++abi has no unwinder and the sandbox keeps exception handling off, so `try`
and `throw` are a compile error. The compiler's module is compiled once per process (about
15 s); the builds and the programs are reproducible to the byte.

What a C or C++ author should know about the target. It is 32-bit: `int`, `long` and
pointers are 32 bits, `long long` is 64, so `long` arithmetic that fits on a desktop may
overflow here. The program's stack is 8 MiB, as on Linux (a local array of a million ints
fits), placed below the data, so an overflow leaves the memory and traps instead of writing
over the globals. A recursion also uses the engine's call stack, 8 MiB as well: a small
recursive function reaches some 90,000 to 170,000 levels. The frame decides, and so does the
node: a frame is larger where traps are explicit checks (a Windows node under hardware stack
protection), and processor architectures differ. One small function returned from 104,850
levels with hardware traps and from 87,375 with explicit checks on the same machine. So a
depth-first walk of a 20,000-node chain runs on every node; one that goes some tens of
thousands of levels deeper may return on one node and end as `call stack exhausted` on
another (a sampled task is then a dispute), and must be written with an explicit stack. Crashes are `RuntimeError` with the trap named on stderr when the program said nothing
(`wasm trap: out of bounds memory access` for a wild pointer or an overflowed stack,
`wasm trap: unreachable code reached` for `abort` and a failed `assert`), and `new` past the
memory cap is `MemoryExceeded` (libc++ says `bad_alloc` and aborts). A write through a null
pointer does not crash: address 0 is memory like any other. `-O2` removes what it can prove
unused, an allocation included.

The raw `wasi` runtime has no module of its own: each task carries one,
compiled by the lab offline (8 MiB at most), and the sandbox prices it and
compiles it on the node (see "Modules a task brings" above; cached by content
hash within a batch). args, stdin, suite and limits work as for any task; a
module that does not validate or cannot be instantiated (an import the sandbox
does not provide) is a `CompileError`, one without `_start` a `RuntimeError`. A
224-byte hello, assembled by hand in the tests (it costs 482,292 units of fuel:
458,752 the price of its 224 bytes, 22,528 because its one function of 13 bytes
is priced as 24, 1,000 its one call into the host, and 12 the call's six bytes
and its six instructions):

```json
{"runtime":"wasi","entry":"hello.wasm","sources":{"hello.wasm":"AGFzbQEAAAABEANgBH9/f38Bf2ABfwBgAAACZwMWd2FzaV9zbmFwc2hvdF9wcmV2aWV3MQhmZF93cml0ZQAAFndhc2lfc25hcHNob3RfcHJldmlldzEHZmRfcmVhZAAAFndhc2lfc25hcHNob3RfcHJldmlldzEJcHJvY19leGl0AAEDAgECBQMBAAEHEwIGbWVtb3J5AgAGX3N0YXJ0AAMKDwENAEEBQQBBAUEIEAAaCwssAQBBAAsmIAAAAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABoZWxsbwo="},"suite":[{"expected_stdout":"hello\n"}]}
```

Honest scope: pure Python with the standard library, Python with numpy, JavaScript, TypeScript
(types stripped), Ruby with its standard library, Lua, C and C++ (no exceptions) built on the
node, and compiled WASI modules. No C extension inside CPython-WASI but numpy's, built in; no
gems beyond the four named.

## What the module carries

This is the first controller that calls a native library in-process (wasmtime,
through the `Wasmtime.Dotnet` binding), so the deployed module carries the
library per platform in the .NET layout its `deps.json` describes:

```
verify.module/
  OutWit.Controller.AI.Verify.dll, .deps.json, controller.json
  OutWit.Controller.AI.Verify.Model.dll, .deps.json
  Wasmtime.Dotnet.dll                      staged by the build
  THIRD-PARTY-NOTICES.txt                  the notice of that one assembly
  runtimes/<rid>/native/                   wasmtime, one data asset per RID
  wasm/                                    the language runtime modules, one data asset
  wasm/python-3.14.6-numpy-2.5.4/          the numpy runtime, a data asset of its own
```

The first four lines are the module: the seven files the build stages, and the only ones the
package ships under `content/module/`. The build keeps a record of what it staged and makes
the package, the module's zip and the manifest's content digest from that record, not from
whatever the staging folder holds: a copy of the natives or of a runtime kept there for local
runs stays where it is and is not packed. The three folders reach a node as data assets.

Two load modes meet this layout. An isolated plugin load context (the server,
the module-load test) resolves the library through the module's `deps.json`.
The module-load test runs on the packaged library: it takes the native asset
the module's own manifest declares for the platform from its release, holds it
to the manifest's size and SHA-256 and unpacks it where the manifest says, so a
manifest without the library fails the test (the language runtimes of that test
are laid out by `verify-assets`, not taken from their archives; that the
archives are that layout packed, byte for byte, is what the `AI Verify archives`
workflow holds).
The cloud client loads controllers into the shared load context, where that
file is never read and a P/Invoke probes only the application folder and the
module root, and where the binding loads once, from the folder of whichever
controller needed it first. So before its first wasmtime call the controller
registers the binding's DllImport resolver (when no other controller did) and a
handler of its load context's `ResolvingUnmanagedDll` event, both answering
with `runtimes/<rid>/native/` of this module (the host's runtime identifier
first, then the portable one) and only with the pinned library. A resolver that
finds nothing answers nothing, the runtime goes on to its own probing and then
to every handler, so another controller that ships no natives, or other ones,
cannot take the library away from Verify (it did on 2.4.5-test.15, when Corpus
0.2.0 came without natives and loaded the binding first).

The sandbox host itself (the resolver, the engine and its settings, the guest
threads, the deterministic linker and the run of one WASI command module) is
shared by the AI controllers as source, not as an assembly: `AI/Shared/Wasm/`,
compiled into each controller as its own internal copy (`OutWit.Controller.AI.Wasm`).
In the shared load context an assembly name loads once and the first controller to
load it wins, so a shared assembly would tie the controllers' versions together; the
one assembly they do share is the binding, pinned to the same version in each. A
startup guard compares the loaded binding with the version the controller was built
against, and the native library the binding loaded with the SHA-256 pinned for the
platform (`WasmHostNatives`): on a mismatch the sandbox does not start, and the
self-check's `wasmtime-native: FAILED` line names both versions or both hashes. Code
the engine compiles is valid for one native library and one trap mode (hardware or
explicit traps); the host keys compiled code by both (`WasmHost.CompiledCodeKey`), and
nothing keeps compiled code past its process.

The natives and the runtime modules are Tier-2 data assets (`ControllerDataAsset`),
fetched by consumers at build time from releases of this repository and verified by
hash: the natives from `wasmtime-v48.0.8-2` (the libraries of `wasmtime-v48.0.8`, byte for
byte, packed again with their third-party notices), the runtimes from a `verify-v<version>` release (`verify-v0.15.0`: a release keeps its tag while its bytes do not change); the nupkg itself stays small. The engine is the
long-term line 48 of wasmtime: the .NET binding 48.0.2 (the `Wasmtime` package) over
the native library of its newest patch, 48.0.8, taken unchanged from the engine's own
release archives (`toolchain/natives.sh` of the asset tool, pinned by the digests the
release states; the build server runs the tests on the same library on each platform).
A patch of the line changes neither an output nor a unit of fuel; a major release would
change fuel, so the fleet stays on one line.

Licences travel with what they are about. Every folder of an asset archive, and the module
itself, has a `THIRD-PARTY-NOTICES.txt`: the third-party software in the folder, what is
linked statically into its module included, and the terms of each part, as a text in the
file or as a licence file beside it that the file names. The files are written by
`toolchain/notices` of the asset tool from pinned sources (an address and the SHA-256 its
bytes must have, or an archive the toolchain already pins); no text is written from memory.
They are committed, and the `AI Verify archives` workflow fails when a committed file is not
what the tool writes. `VerifyRedistributedFilesTests` holds the module, the package, each
runtime folder and each native library folder to a pinned list of files, and every file of a
folder to its notices: a file that is added without a notice fails there by its name. What
the lists rest on, and where they end:

- what a module links statically was read from the module (its strings and its compiler's
  mark) and from the build that made it; the list of the engine's Rust crates (164 of
  others) was read from the engine's own vendored source archive by following manifests,
  features and platforms from its C API library, not by asking the compiler, so a crate
  can be listed of which the linker kept nothing;
- the Rust standard library's own account of the parts it took from others
  (`COPYRIGHT-library.html` of a Rust release) is pointed to, not reproduced;
- a crate whose package carries no licence text (three of the engine's) is listed with the
  licence its manifest states.

In short, and the notices files are what counts
(wasmtime and its .NET binding: Apache-2.0 WITH LLVM-exception, the Rust standard library
and the engine's crates under their own terms, mostly MIT or Apache-2.0; CPython: PSF, with
expat, HACL\* and mimalloc: MIT, libmpdec: BSD-2-Clause; QuickJS-NG: MIT;
Ruby and its bundled gems: Ruby's own terms or BSD-2-Clause, with OpenSSL: Apache-2.0,
libyaml: MIT, zlib: Zlib, wasi-vfs: Apache-2.0 WITH LLVM-exception; the ruby.wasm
port: MIT; Lua: MIT, with Binaryen: Apache-2.0; sucrase and the six packages bundled with it: MIT, Apache-2.0 and
others, each named in the licence file beside the bundle, with esbuild: MIT; LLVM, clang, lld, libc++, compiler-rt and YoWASP's build: Apache-2.0, LLVM's WITH LLVM-exception;
wasi-libc: Apache-2.0 WITH LLVM-exception, Apache-2.0 or MIT, with cloudlibc's BSD-2-Clause
and musl's MIT inside; numpy: BSD-3-Clause with 0BSD, MIT, Zlib and CC0-1.0 parts, each named in
`LICENSE-numpy.txt`, and Dragon4: MIT; the numpy build's recipes and two numpy patches adapted from wasi-python:
Apache-2.0, `LICENSE-wasi-python.txt`; the patches themselves are in the archive, and its
`BUILD.txt` names them).

`VerifySelfCheck.Run()` reports, from inside a loaded module, where the binding
and the runtimes resolved from and whether one task per runtime ran — the
module-load test calls it through the real isolated loader, and an operator
can call it as a doctor-style check.

## Tests

`OutWit.Controller.AI.Verify.Tests`: the wire-layout guard (every member's
order, name, type and default, and the bytes of an image), the schema guards
(the parser takes every field a schema lists and no other, and every number's
range), the numbers of this file, of the capabilities document and of the
safety page against the code's constants,
the parser at its edges and at its bounds, the sandbox verdict, adversarial, hygiene, raw-WASI,
Python-layer, numpy, Ruby, Lua, QuickJS, TypeScript and C/C++ suites, node
protection (`VerifyNodeProtectionTests`) and the price of a module's shape,
the same result on every node (`VerifySameResultTests`: file names, file
records, seeds, a runtime that is missing), the
tree hash, the worker pool (cancellation
included) and the
benchmark, the taskset, sampling and integrity pipelines, the caps of the plan,
the server's intake and rulings (`VerifyTrustBoundaryTests`), the walk of a
result package's frames against what a writer writes and against frames
changed at random (`VerifyResultFramesTests`), results outside what their task
allows (`VerifyResultIntakeTests`), fan-outs that came back short
(`VerifyShortFanOutTests`) and whole runs against lying nodes
(`VerifyLyingNodesTests`), the two trap modes compared
(`VerifyTrapModeTests`), the hostile taskset of the fleet proofs held to its
verdicts (`VerifyHostileKitTests`), a scan of every document of
hostile and failed runs for anything of a node (`VerifyAnonymityTests`), the
package format,
plan, transfer (a link that drops and comes back included) and the share's
progress, the end-to-end runs
of every bundled script through the engine on in-process nodes, the
module self-containment proof, and the shared wasm host on its own (its
verdicts and folders, exit statuses of every size on every runtime, the fuel of a
hello pinned per runtime, the binding and natives guards in a client-like host, the
native resolution with a second carrier of the host loaded first, and the
repository pins of the binding and the natives). The sandbox runs only on the
pinned native library, so the test projects take it from `@Data/natives`
(`toolchain/natives.sh` of the asset tool) in place of the package's copy after the
build (`AI/Shared/WasmHostPinnedNatives.targets`). The sandbox suites need the pinned runtime set
(`verify-assets build OutWit.Controller.AI.Verify.Tests/@Data/runtimes`;
`@Data/` is gitignored) and Assert.Ignore without it; the `AI Verify` workflow
builds the set and runs everything on Linux, macOS and Windows, where a runtime
that does not resolve is a failure, not a skip. A rig loads the modules it
names and no other (`VerifyRigModules`): a module of another controller in the
shared staging folder is not loaded, and a module that is not byte for byte
this build's fails the rig by name. The proof that a host under hardware stack
protection survives a guest's trap runs only on a machine that enforces that
protection; elsewhere its test is skipped and says so. Nothing fails for that
(the build server's Windows machines are not one kind); instead a Windows run
states which case it was in one line of its log and of the job's summary,
`stack-protection proof: shadow stacks on` or `stack-protection proof: shadow
stacks off`, and a release is cut from a commit whose Windows job states the
first.

What the controller hands on and what guards its way to a node have tests of
their own: `VerifyRedistributedFilesTests` (the module as the build recorded
it, its zip, the package's `content/module`, each runtime folder and each
native library folder against a pinned list of files, and every file against
its folder's notices), `VerifyBindingNoticeTests` (what the module's notices
say of `Wasmtime.Dotnet.dll`, line by line, against the package the build
restored and the file the module carries), `ControllerStagingTests` (the
shared build files on a solution of their own: a build records what it
stages, removes only what it staged itself and no longer does, and packs what
it recorded, so a file put into a module folder by hand is neither lost nor
shipped), `VerifyAssetArchiveTests` (the asset tool refuses an archive entry that names
a place outside its folder), `VerifySupplyChainTests` (the workflows and the
build files, read as text: modules compared with a rebuild before a merge,
archives held to their pins, actions by commit, no floating version, a
download without a pin stops a build), and `VerifyLoggerGuardedTests` with
`VerifyLoggerFateTests` (a host's logger that throws, or that cannot take two
lines at once, does not decide a package's fate).

Load tests (`Category=Load`, not in the sweep; the `AI Verify` workflow runs
them once a week and on request): a thousand tasks at the
machine's degree, thousands of runs with bounded memory, hundreds of distinct
raw modules against the bounded module cache, a hundred thousand lines
through the parser and the planner and a hundred thousand results through
every writer, thousands of tasks through `VerifyRun` with sampling on
the in-process nodes, and a hundred thousand tasks from a 200 MiB taskset
through `VerifyRun` with the memory and every payload measured
(`VerifyScaleLoadTests`) — `dotnet test --filter Category=Load`.
