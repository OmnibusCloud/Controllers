# Verify: what the controller can do

A reference for building on the Verify controller. It covers:

- how to call it, and what goes in and what comes out;
- which languages run, at what cost and within which limits;
- what is guaranteed about the results;
- where the edges are.

Every number on this page was measured, in the controller's tests or on a test fleet. The
[README](../README.md) explains how things work inside; [safety.md](safety.md) is the same
sandbox told for the people who lend their computers.

Version described: **Verify 0.17.0** (with Verify.Model 0.4.0 and Verify.Scripts 0.7.0), for the
test server. 0.15.0 is proven on a five-node test fleet (Windows and Linux; 0.11.3 on six nodes
with macOS), and the fleet figures on this page (scale, medians, pool rate) were measured before
0.13.0. None of these versions is on nuget.org or on the live network.

What 0.17.0 changed for an application:

- **Every fuel figure moved; no verdict and no output did.** A call into the host costs 1,000
  units besides the bytes it moves, and the modules the engine compiles on a node (`wasi` and
  C/C++) pay for their smallest functions (sections 9 and 10, with the figures before and
  after). A task budgeted within a few percent of its need should be given the difference.
- **More of a task's module is refused before it is compiled** (section 9): a function type of
  more than 128 parameters or 8 results, a block of more than 8 values, a function of more than
  16,384 locals or 128 KiB, a module of more than 32,768 functions, and a module with a start
  function. Each is a `CompileError` with fixed words. No program a C, C++ or Rust compiler
  writes for this target is near these, a start function aside.
- **A C/C++ build may write 128 MiB in all**, its objects and its program together (it was 64
  MiB an object and no bound on their sum).
- **A deep recursion is a stated limit** (sections 9 and 14): how deep one goes before the
  engine's stack ends depends on the node.
- **The fuel of a CPython task is no longer said to drift between hosts** (section 10): it has
  been one number on Windows, Linux and macOS since 0.13.0, and the tests now hold it to that.
- **No document, no schema and no script changed.** The model and the scripts are the packages
  they were.

What 0.16.0 changed for an application:

- **New document versions.** `verify-report/3` and `verify-integrity/3` (section 6). Every
  field of the earlier versions is there with the name and the meaning it had. New in both
  summaries: `sampling_rate` (the rate that was asked for; `null` when the script did not say),
  `sampling` (`as_asked`, `short`, `none`, `not_stated`) and `unanswered_packages`. New in the
  integrity document: `fan_outs`, a record for every fan-out, and `round` in every pair. An
  application that holds a document to its `schema` tag takes the new tags.
- **The report of a taskset is the same bytes for the same options.** It now states the
  sampling rate, so the report of one taskset at two rates differs in that field. In a pool of
  one machine nothing runs twice, and a run that asked for sampling says `short`.
- **The scripts changed.** The repair round runs under the sampling of the first fan-out,
  `Verify.Disputes` is handed the repair round too, and `Verify.Settle` the sampling rate
  (section 3). The job variables an application reads are the same. A script of Verify.Scripts
  0.6.0 still runs and settles as it did; its documents say `not_stated`.
- **More is refused of a node.** A result outside what its task allows (fuel beyond the budget
  of its runs, memory beyond the cap, a time below zero, stdout beyond the cap, case records
  that are not those of the suite, a verdict its case records do not give) is refused as
  `content`, and a package with a length or a count that does not fit its own bytes, or with
  text that is not valid Unicode, as `unreadable` (section 10). The task then goes out once
  more. No result of an honest node is outside these.
- **Nothing else moved.** No verdict, no fuel figure and no output of a run without lying or
  failing nodes changed, and its provenance differs only in the controller version it names;
  the model is the package it was.

What 0.15.0 changed for an application:

- **Nothing in a run.** A taskset gives the verdicts, the fuel figures and the report it gave
  under 0.14.0, byte for byte, and the other documents with the controller's version as their
  only difference; no schema and no document version moved; the model and the scripts are the
  packages they were.
- **A node's log cannot fail a package.** On a node whose log throws or cannot take two lines
  at once (cloud clients up to 2.2.5), a share used to fail now and then with the log's own
  error in the job's status, and the platform gave the work to another node. The work now
  completes, and a package that fails for a reason of the node's own says so in the same fixed
  words as before (section 6).
- **What a deployment carries changed** (section 13): the archives of the language runtimes
  and of the native engine library are new ones, with a `THIRD-PARTY-NOTICES.txt` in every
  folder; the files inside that a program runs on are the same bytes.

What 0.14.0 changed for an application:

- **A taskset is held to its schema, exactly.** A line with a field the schema does not have (a
  misspelt `expected_stdout` is one), a field or a source name given twice, a value of another
  type (a suite that is not an array, a `null`), a number outside its range (a limit of 0), a
  NUL character in a program or an argument, or text that is not valid Unicode is refused, by
  the field's name. Before, such a line ran with the field ignored. The same holds for a
  checker spec (section 4).
- **A runtime id is matched to the letter.** `PYTHON-3.14.6` is an unknown runtime.
- **New bounds on a taskset.** A line of 64 MiB, 1,024 sources, 1,024 arguments and 1,024
  cases a task, 4,194,304 tasks a taskset (sections 4 and 9).
- **The preflight's answer is bounded.** At most 32 messages of a kind, of 240 characters each,
  with one line that says how many more there are (section 3).
- **A new document version.** `verify-capabilities/3`: the capabilities document carries these
  bounds as numbers, in a section of their own, `taskset` (section 3). Every field of
  `verify-capabilities/2` is there with the name and the meaning it had; an application that
  holds the document to its `schema` tag takes the new tag.
- **Nothing else moved.** A taskset the schema allowed is cut into the same packages and gives
  the same documents as under 0.13.0; no fuel figure and no verdict changed.

What 0.13.0 changed for an application:

- **Fuel is the one budget.** `Timeout` means out of fuel. The wall clock gives no verdict, and
  `limits.wall_ms` is accepted and is not a limit any more. The default budget is 20e9 (it was
  50e9) and the ceiling 240e9 (it was 2e12). A call into the host costs fuel by the bytes it
  moves, so every fuel figure moved (sections 9 and 10).
- **Two new verdicts.** `NoResult` (no node gave a verdict) and `Disputed` (the executions
  disagreed and three more did not settle it). `RuntimeUnavailable` is no longer produced
  (section 7).
- **New document versions.** `verify-report/2`, `verify-provenance/2`, `verify-integrity/2`,
  `verify-capabilities/2` (sections 3 and 6).
- **Tasksets are checked harder.** Source names must be portable for every runtime, and an
  unknown runtime id refuses the taskset (section 4).
- **Stricter cases.** A suite case passes only when the program ended by itself, and the cases
  of a task share its stdout cap (sections 4 and 9).
- **A task's own module is priced.** Compiling a `wasi` module, or a program built from C or
  C++, is paid from the task's fuel (section 9).
- **Random streams.** Every seed but 0 has a new stream (section 9).

## Contents

1. [What Verify is for](#1-what-verify-is-for)
2. [At a glance](#2-at-a-glance)
3. [Calling Verify from an application](#3-calling-verify-from-an-application)
4. [The taskset](#4-the-taskset)
5. [Generate and check](#5-generate-and-check)
6. [What comes back](#6-what-comes-back)
7. [Verdicts](#7-verdicts)
8. [Runtimes](#8-runtimes)
9. [Limits and the sandbox](#9-limits-and-the-sandbox)
10. [Determinism and integrity](#10-determinism-and-integrity)
11. [Scale, delivery and the nodes](#11-scale-delivery-and-the-nodes)
12. [Demo kits](#12-demo-kits)
13. [Versions and what a deployment carries](#13-versions-and-what-a-deployment-carries)
14. [Known limits](#14-known-limits)

## 1. What Verify is for

Verify runs untrusted programs against specifications on anonymous computers and reports, for
each program, what it printed and whether it passed. A program cannot see the machine it runs
on, and every honest machine gives the same answer for it. Generation and verification are the
same activity with different programs, so one primitive carries several products:

| Product | What a task is | What a lab gets |
|---|---|---|
| Code against tests (RLVR rewards) | a candidate solution and its test cases | a verdict per candidate: the reward |
| Generate and check | a generator program, then a checker run on its output | a corpus of candidates and a verdict for each |
| Math with a computer algebra system | a sympy program that checks an answer, or makes problems with answers | verified problems and answers |
| Compiled modules | a WebAssembly module the lab compiled itself | a verdict per module |

## 2. At a glance

| | |
|---|---|
| Languages | Python 3.14 (also with sympy, also with numpy), JavaScript, TypeScript, Ruby 3.4, Lua 5.4, C and C++ compiled on the node, and any compiled `wasm32-wasip1` module (Rust, Zig, TinyGo, …) |
| Sandbox | WebAssembly (wasmtime, long-term line 48): no files of the machine, no network, no processes, no threads; its own clock and random source |
| Limits per task | fuel (one unit per instruction, 1,000 a call into the host and one a byte the call moves) 20e9 by default and 240e9 at most; memory 256 MiB / 2 GiB; stdout 256 KiB / 8 MiB. No wall-time limit: the clock is a node's own backstop |
| Determinism | the verdict report and the outputs are the same bytes on every fleet, every placement and every run, as long as every node did its work and told the truth |
| Integrity | a share of the packages runs twice on two different machines and the pairs are compared; tasks whose copies disagree run three more times and a majority settles them; a task nobody answers is sent out once more |
| Scale | a million small tasks in 22 min 41 s on six nodes; 100,000 in 2 min 55 s; 10,000 in 23 s (measured before 0.13.0) |
| Platforms | nodes on Windows x64, Linux x64 and macOS arm64, all giving the same results |

## 3. Calling Verify from an application

### The flow

Every input is a blob and every output is a blob. The application:

1. uploads the taskset (and the checker spec, for generate-and-check);
2. submits a script with the blob ids and the options;
3. waits for the outputs;
4. downloads them.

With the platform SDK (`OutWit.Cloud.SDK`; the options type is in
`OutWit.Controller.AI.Verify.Model`):

```csharp
await using var client = new WitCloudClient(serverUrl, identityUrl, apiKey);   // a wit_sk_ application key
await client.ConnectAsync();

var taskset = await client.Blobs.UploadBlobAsync(File.ReadAllBytes("taskset.jsonl"), "taskset.jsonl");
var options = new VerifyOptionsData { BatchSize = 32, SamplingRate = 0.1 };

var handle = await client.Scripts.SubmitAsync("VerifyRun", [taskset, options]);

var report = await handle.WaitAsync<Guid?>("report");       // also "outputs", "provenance", "integrity"
if (report.Status == ProcessingJobStatus.Completed)
{
    var bytes = await client.Blobs.DownloadBlobAsync(report.Result!.Value);
    // the verdict report, JSON: section 6
}
```

`AI/OutWit.Controller.AI.Verify.Live/Program.cs` (the `verify-live` tool) is a complete,
working client: it runs, generates, checks, reads the diagnostics, and saves every output.

### The scripts

They are bundled in `OutWit.Controller.AI.Verify.Scripts` and run on the server; nodes run
`Verify.ExecutePackage`, the one way work reaches a node.

| Script | Inputs | Outputs | Use it for |
|---|---|---|---|
| `VerifyRun` | taskset blob, options | `report`, `outputs`, `provenance`, `integrity` | verdicts of programs (RLVR rewards, code against tests) |
| `VerifyGenerate` | taskset blob, options | `corpus`, `report`, `provenance`, `integrity` | collecting what programs print (a generated corpus) |
| `VerifyGenerateAndCheck` | generators blob, checker blob, options | `report` (the checks), `corpus` (the candidates), `provenance`, `integrity` | generating candidates and checking each with one checker program |
| `VerifyPreflight` | taskset blob, options | `result` | validating a taskset before a run |
| `VerifyRuntimeDiagnostics` | none | `result`, `capabilities` | what the deployment offers: runtimes, limits, the pool |

The job variables an application reads are the same as before 0.13.0. Inside, a run now has up
to three fan-outs (section 10):

1. the first one, with a share of the packages in two copies;
2. the repair round: tasks without a verdict go out once more, the same share of their
   packages in two copies;
3. the dispute round: tasks whose executions disagree, in either of the two, run three more
   times on different machines.

`Verify.Disputes` is given the first fan-out and the repair round (four arguments; with two it
looks at the first fan-out alone, as before 0.16.0). `Verify.Settle` is given the three rounds
and, as its seventh argument, the sampling rate they ran under; without it the repair round is
taken as one execution a package and the documents say that the sampling was `not_stated`.
`Verify.Settle` then rules on every task and writes the four documents once, into one ledger
blob; `Verify.Collect`, `Verify.CollectOutputs`, `Verify.Provenance` and `Verify.Integrity`
hand them out from the ledger. The [README](../README.md) shows the scripts as they are.

### The options (`VerifyOptionsData`)

| Field | Default | Meaning |
|---|---|---|
| `DefaultLimits` | the controller's defaults (section 9) | limits for tasks that do not set their own. Its wall time is accepted and is not a limit |
| `BatchSize` | 32 (0 means the default) | tasks per batch, clamped to 4,096; a batch shares one compiled runtime module. A batch is also cut earlier by its tasks' text and output caps (section 11) |
| `MaxDegree` | 16 (0 means the default) | at most this many sandboxes at once on a node (never more than its cores or its memory allows) |
| `SamplingRate` | 0 | the share of packages run twice on another machine for integrity, 0 to 1: rounded up to a whole package, one at least for a rate above 0, in the first fan-out and in the repair round |
| `ReportFormat` | — | reserved |

### Before a run

`VerifyPreflight` reads the taskset and answers without running anything:
- `WellFormed`, `TaskCount` (the lines that are tasks), `BatchCount`;
- `RuntimeIds` and `UnknownRuntimeIds`: the ids the taskset names, an unknown one cut to 64
  characters and shown in plain characters only; at most 32 unknown ids are listed;
- `EstimatedInputBytes`;
- `Messages`: line errors (`line 7: unknown field 'suite[0].expected_stdoutt'`; a source name
  that is not portable is one, by the name), one message for every listed unknown runtime, one
  when the taskset or the options set a wall time, the limits and the batch size that will be
  clamped, and a task whose text is over 8 MiB.

The answer is bounded whatever the taskset: at most 32 messages of each kind (line errors,
clamped limits, tasks that cannot be planned), each at most 240 characters, and after them one
line such as `and 9968 more malformed line(s) not shown`. A preflight never returns more than
134 messages.

`WellFormed` is false for everything `Verify.Split` would refuse the taskset for: a line the
schema does not allow, a source name outside the portable set, an unknown runtime id, a task
that is too large, more tasks than a taskset may have, an empty taskset.

`VerifyRuntimeDiagnostics` returns the `capabilities` document (schema
[`verify-capabilities.schema.json`](verify-capabilities.schema.json), `verify-capabilities/3`).
It is what an application's runtime picker, limit fields and capacity line need:

| Field | What it holds |
|---|---|
| `controller`, `wasmtime` | the versions |
| `runtimes[]` | `id`, `language`, `sha256`, `available`, and `notes`: one sentence per fact an author must know before writing for that runtime (for example numpy's 32-bit default integer) |
| `defaults`, `ceilings` | the limits (section 9); no wall time among them |
| `time` | how fuel relates to time: `reference_fuel_per_second`, and the two numbers a node's wall-clock backstop is made of, `backstop_base_ms` and `backstop_fuel_per_second` (section 9) |
| `sources` | the source-name rule: `name_pattern`, `max_name_bytes`, `max_depth` (section 4) |
| `modules` | what a task's own module is held to: `max_bytes`, `max_nesting`, `compile_fuel_per_byte` (section 9) |
| `packages` | how a run is cut: `target` count; `max_tasks`, `max_bytes` and `max_output_bytes` per package; `max_batch_tasks` and `max_batch_output_bytes` per batch (section 11) |
| `taskset` | what a taskset may hold and what a preflight answers (the table below; sections 4 and 9) |
| `pool` | the fleet as totals: `nodes`, `measured` (nodes with a benchmark), `workers`, `rate` (reference tasks per second). No node is ever named |

The `taskset` section is new in `verify-capabilities/3`. It holds the bounds of what the server
reads from a lab, so an application reads them from the document and carries no copy of its
own. In this version they are:

| Field of `taskset` | Value | What it bounds |
|---|---|---|
| `max_line_bytes` | 67,108,864 | the bytes of one line of a taskset (64 MiB), and of a checker spec |
| `max_tasks` | 4,194,304 | the tasks of a taskset |
| `max_sources` | 1,024 | the source files of a task |
| `max_args` | 1,024 | the arguments of a task, and of a case |
| `max_suite_cases` | 1,024 | the cases of a suite |
| `max_preflight_messages` | 134 | the messages of a preflight's answer, whatever the taskset |
| `max_preflight_messages_per_kind` | 32 | the messages of one kind a preflight shows before the line for the rest |
| `max_preflight_message_length` | 240 | the characters of one message of a preflight |
| `max_preflight_unknown_runtimes` | 32 | the unknown runtime ids a preflight lists |

A taskset outside the first five is refused (section 4). The most text a task may have is
`packages.max_bytes`.

### While it runs

A node reports its share about twice a second:
- first the delivery: `receiving tasks: 12/215 packages, 3.1/14.8 MB`;
- then the work: `51200/220160 tasks`.

The job's progress is these counts. Verdicts come at the end, when the report is written.

## 4. The taskset

JSON lines: one task per line, no enclosing array, UTF-8. Schema:
[`verify-taskset.schema.json`](verify-taskset.schema.json).

| Field | Required | Meaning |
|---|---|---|
| `index` | no | the task's index in every output, 0 to 2,147,483,647; defaults to the line's ordinal. One task an index |
| `runtime` | yes | a runtime id (section 8), to the letter; a taskset that names an unknown id is refused when it is split |
| `entry` | no | the file to run, one of the source names; defaults to the first source |
| `sources` | yes | file name → content, at most 1,024 files, no NUL character in a content. The names must be portable (below). What is done with the files depends on the runtime (below) |
| `args` | no | arguments after the entry, at most 1,024, none with a NUL character |
| `stdin` | no | standard input of a run without a suite; any text |
| `seed` | no | the seed of the sandbox's random source, a 64-bit integer; default 0 |
| `suite` | no | at most 1,024 test cases: `stdin`, `args` (appended to the task's), `expected_stdout` (exact), `expected_exit` (default 0) |
| `limits` | no | `fuel`, `memory`, `stdout_bytes`, `stderr_bytes`, each 1 or more; clamped to the ceilings. `wall_ms` is deprecated: still accepted, and not a limit (section 9) |

The controller accepts exactly what the schema allows. A line is refused, by the name of what
is wrong, when it has:

- a field the schema does not have, at any level: `unknown field 'limitz'`,
  `unknown field 'suite[0].expected_stdoutt'`. A misspelt expectation can never be dropped
  and turn a test into a pass;
- a field given twice (`field 'sources' is given twice`), or two sources of one name;
- a value of another type: `'suite' must be an array of cases`, `'stdin' must be a string`
  (a `null` is another type), `'limits.fuel' must be an integer`. An integer may be written
  any way a schema takes for one (`5`, `5.0`, `5e9`);
- a number outside its range: `'limits.fuel' must be 1 or more`, `'index' must be 0 or more`;
- a NUL character (U+0000) in a source, an argument or the entry: a program is handed to its
  interpreter as a C string, and a NUL would cut it there. In `stdin` and `expected_stdout` a
  NUL is data;
- text that is not valid Unicode (half of a surrogate pair written as an escape, bytes that
  are not UTF-8);
- more than 1,024 sources, arguments or cases; a line over 64 MiB; text over 8 MiB.

A taskset has at most 4,194,304 tasks. A message names a field of the lab's own only in plain
characters and cut to 64 of them.

How a runtime takes the sources:

| Runtimes | `sources` |
|---|---|
| `python-*`, `quickjs-0.15.1`, `typescript-sucrase-3.35.1`, `ruby-3.4.1` | the entry is the program, passed inline; other files are not available to it |
| `lua-5.4.8`, `clang-22.1.0` | every source is a file in a fresh read-only folder that is the guest's whole file system, so headers and `require`d modules sit beside the entry. A file is found under the exact name it has and under no other, on every node (section 9) |
| `wasi` | the entry is the module's bytes in base64, 8 MiB of module at most; its name is the guest's `argv[0]` |

Source names must be portable, for every runtime. A taskset with another name is refused when
it is split, and the preflight reports it by the name. The rule:

- the characters `A-Z a-z 0-9 . _ - +`, with `/` between folders;
- at most 128 bytes in all and 8 segments;
- no empty segment, no `.` or `..`, no segment that starts with `-` or ends with `.`;
- no segment that Windows reserves for a device (`CON`, `NUL`, `COM1`, …);
- no two names that differ only in case, and no name that is a file and a folder of another.

A name with a character outside ASCII is refused. As a regular expression (the capabilities
document's `sources.name_pattern`): `^(?!-)[A-Za-z0-9._+-]+(/(?!-)[A-Za-z0-9._+-]+){0,7}$`.

A suite passes when every case does. A case passes when the program ended by itself (it
returned or called exit) with `expected_exit` and, if `expected_stdout` is given, printed
exactly that. A crash is not an exit: `expected_exit: -1` does not pass on one. A case that is
met passes whatever the program wrote to stderr. The first failing case sets the task's
verdict. The cases of a task share its stdout cap (section 9).

Examples, one line each:

```json
{"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"a=int(input());b=int(input());print(a+b)"},"suite":[{"stdin":"2\n3\n","expected_stdout":"5\n"}]}
{"runtime":"quickjs-0.15.1","entry":"m.js","sources":{"m.js":"console.log(6*7)"}}
{"runtime":"lua-5.4.8","entry":"main.lua","sources":{"main.lua":"local u = require('lib.util') print(u.twice(21))","lib/util.lua":"return { twice = function(x) return 2 * x end }"}}
{"runtime":"clang-22.1.0","entry":"main.cpp","sources":{"main.cpp":"#include <iostream>\nint main(){ std::cout << 42 << '\\n'; }"},"limits":{"fuel":5000000000}}
```

## 5. Generate and check

`VerifyGenerateAndCheck` takes two inputs.
- **The generators:** an ordinary taskset whose outputs are the candidates.
- **The checker spec** (schema [`verify-checker.schema.json`](verify-checker.schema.json)): one task, without an index, a seed, a stdin or a suite, plus `expected_stdout` and `expected_exit`. It is held to its schema as a taskset line is: a misspelt `expected_stdout` fails the job by its name (it used to leave "exit code 0" as the whole rule, and every candidate passed).

How it runs:
- The generators run once each. They are not sampled (nothing of theirs is compared), but a generator without a verdict is sent out once more. Their run is settled into a ledger of its own.
- The checks are made from that ledger: one settled candidate a generator, the same one the corpus holds.
- Every generator that passed and printed something becomes one run of the checker, with that output on stdin and under the generator's index. The check report lines up with the generators.
- A generator that failed, printed nothing, printed past its cap or has no result is not checked, but is still in the corpus.
- The checks run like the tasks of `VerifyRun`: sampled, repaired and, where copies disagree, settled by three more (section 10). The `report`, `provenance` and `integrity` are the checks'.

The recommended checker contract is to print the verdict and exit 0 (`OK`, or `MISMATCH` and a
reason). A crash of the checker itself then stays distinguishable as a `RuntimeError`.

The math kit (section 12) is a complete example: 38 sympy generators across six families of
problems, and one checker that verifies every answer by another route.

## 6. What comes back

| Output | Schema | Content | Identical across runs and fleets |
|---|---|---|---|
| `report` | [`verify-report.schema.json`](verify-report.schema.json), `verify-report/3` | `summary` (`total`, `pass`, `pass_rate`, counts per verdict, `rejected_results`, `disputes`, `disputes_resolved`, `sampling_rate`, `sampling`, `unanswered_packages`) and `results[]`: `index`, `verdict`, `exit`, `reason` (only for `NoResult`), `cases[]` (`case`, `passed`, `verdict`) | yes, for the same options, as long as every node did its work and told the truth: the run's identity |
| `outputs` / `corpus` | [`verify-corpus.schema.json`](verify-corpus.schema.json) | one JSON line per task: `index`, `verdict`, `exit`, `reason` (only for `NoResult`), `stdout`, `stderr`, the truncation flags, and per case its `exit` and `stdout` | yes, on the same condition |
| `provenance` | [`verify-provenance.schema.json`](verify-provenance.schema.json), `verify-provenance/2` | per accepted batch: `round` (`main`, `repair`, `dispute`), platform (`windows-x64`, …), wasmtime and controller versions, runtime and its hash, fuel; per task: `fuel`, `peak_memory`, `wall_ms`, and `fault` for a task the node could not run | no: these are metrics (section 10) |
| `integrity` | [`verify-integrity.schema.json`](verify-integrity.schema.json), `verify-integrity/3` | `summary` (the counts below), the `fan_outs` (what each round was asked for and what came back, in packages), the `pairs` with what differs, and the `disputes` with how each ended | no: which packages are sampled is random |

What the documents say of a run that did not go well:

- **A task without a result** is `NoResult` with a `reason`: `wall_clock` (a node's backstop
  ended the run), `node_fault` (a node could not run it) or `no_answer` (nothing the server
  accepts came back). Its outputs line has empty `stdout` and `stderr`.
- **A task whose executions were not reconciled** is `Disputed`. It is not a pass, and its
  outputs line has empty `stdout` and `stderr`.
- **`pass_rate`** is `pass / total`: both kinds are in the total.
- **The report's `rejected_results`, `disputes` and `disputes_resolved`** are zero in a run
  without lying or failing nodes.
- **A fan-out that came back short** shows in three fields of the report's summary, and of the
  integrity document's: `sampling_rate` is the rate that was asked for, `sampling` says whether
  it took place, and `unanswered_packages` counts the task packages no accepted result came
  back for, every fan-out together. `sampling` is `as_asked` when as many packages came back
  with two accepted results as the rate asks for, in the first fan-out and in the repair
  round; `short` when fewer did; `none` at a rate of 0; `not_stated` when the script did not
  hand the rate to `Verify.Settle`. A run without lying or failing nodes on two machines or
  more says `as_asked` (or `none`) and has no unanswered package.

The integrity summary counts: `tasks`; `sampled`, `compared`, `mismatches`, `mismatch_rate`
(the pairs of the first fan-out, and of the repair round for the tasks the first left without
a verdict); `agreed` (tasks accepted on two or more executions that
agree) and `single_execution` (tasks accepted on one execution, which rest on one machine: a
task that was not sampled is one of them); `disputes`, `disputes_resolved`, `disputed` (the
disputes that stand); `repairs` (tasks sent out once more);
`no_result`; `rejected_results`, and `rejected` by reason (`unknown_ticket`, `duplicate`,
`unreadable`, `version`, `content`, `surplus`); `sampling_rate`, `sampling` and
`unanswered_packages` as in the report.

`fan_outs` has a record for every round the script ran (`main`, `repair`, `dispute`):
`packages` (the task packages it gave out), `copies` (the executions a selected package was
asked for: 2 where a share is sampled, 3 in a dispute, 1 in a repair round that asked for each
package once), `selected` (the packages asked for that many: what the rate asks for, or all of
them in a dispute; `null` in the first fan-out when the script did not state its rate),
`complete` (the packages that came back with that many accepted results) and `unanswered`
(the packages with none). It names no package: the platform picks the sampled ones at random
and the server does not learn which.

For a task with a suite, the task's own `stdout` is the last case's.

A task has one record in the report and in the outputs, however often it ran. The server never
chooses between two executions: it accepts executions that agree, and has those that disagree
run again (section 10).

The documents never name a machine. The provenance has no degree and no wall time of a batch:
with the memory cap and the degree the lab's to choose, a node's cores and memory could be read
off them. What a node says of its own trouble reaches a document only as one of the reason
codes above; a path, an account name or the text of an error stays in the node's own log. A
node's log that fails (it throws, or cannot take two lines at once, as the log of cloud clients
up to 2.2.5) changes nothing of this since 0.15.0: the line is lost, the package completes or
fails in the same fixed words, and the log's own error goes nowhere. The
program's own stdout and stderr are the program's and stay as they are.

## 7. Verdicts

| Verdict | Meaning | Typical cause |
|---|---|---|
| `Pass` | the run, or every case, did what was expected | |
| `Fail` | it ran, but a case printed the wrong thing or exited with the wrong code (not a crash) | a wrong answer |
| `CompileError` | the program never ran | a C/C++ compile or link error, or a build whose product is over 64 MiB; a module that does not validate or imports what the sandbox does not provide; a raw module over 8 MiB; a function that nests blocks deeper than 2,048, a module past another bound of a task's module, or one with a start function (section 9); a C/C++ build that writes more than 128 MiB in all |
| `RuntimeError` | it crashed, or exited non-zero where 0 was expected (any status, 126 and more included) | an exception, a panic, `abort`, an out-of-bounds access, a stack overflow, `exit(200)` |
| `Timeout` | the fuel ran out, and nothing else. It is the same on every node | an endless or too slow program; a module whose compile the budget does not hold (section 9) |
| `MemoryExceeded` | the program's allocator was refused memory | Python's `MemoryError`, Lua's "not enough memory", C++'s `bad_alloc` |
| `OutputExceeded` | stdout passed its cap; the write that passed it ended the run | printing in a loop; a suite whose cases together print more than the task's cap |
| `NoResult` | no node gave a verdict, also when the task was sent out once more. It says nothing about the program. The documents carry a `reason` | `wall_clock`: a node's backstop ended the run; `node_fault`: a node could not run it; `no_answer`: nothing the server accepts came back |
| `Disputed` | the executions disagreed, and three more on different machines did not give one answer from at least two of them. Not a pass; no outputs | copies that keep disagreeing; copies of the dispute round that did not come back; a lead that could be one machine answering twice (section 10) |
| `RuntimeUnavailable` | not produced since 0.13.0; the name stays so that documents of earlier runs can be read | |

What became of `RuntimeUnavailable`: an unknown runtime id refuses the taskset when it is split.
A node that lacks a runtime, or whose copy fails its SHA-256 pin, refuses the whole package, and
the platform gives the package to another node.

When a crash is a WebAssembly trap and the program wrote nothing to stderr, the sandbox names
the trap there in fixed words:

| stderr | Cause |
|---|---|
| `wasm trap: unreachable code reached (abort, a failed assertion, a trap instruction)` | `abort()`, a failed `assert`, a Rust panic |
| `wasm trap: out of bounds memory access (a pointer outside the memory, or a stack that overflowed)` | a wild pointer, an index far past an array, a C stack overflow |
| `wasm trap: call stack exhausted` | a recursion deeper than the engine's call stack |
| `wasm trap: integer division by zero`, `integer overflow`, `invalid conversion to integer` | the arithmetic traps |
| `wasm trap: call through a null function pointer`, `call through a function pointer of the wrong type` | a broken indirect call |

The sandbox adds no words of its own to stderr for a `Timeout` or an `OutputExceeded`: stderr
holds what the program wrote before it was stopped.

A wild pointer handed to a host call (`random_get`, `clock_time_get`) is answered with WASI's
`EFAULT` (21) inside the program. It used to fail the whole batch.

## 8. Runtimes

Each runtime is a WebAssembly module pinned by SHA-256 in the controller's catalog, and the same
bytes run on every platform. A batch shares one compiled module.

The costs below are medians over the demo tasks on the test fleet (`provenance`). They give
the order of magnitude a planner should expect. The medians were measured before 0.13.0. Since
then a task's fuel also counts the bytes its calls into the host move (section 10), so today's
figures are higher by the bytes a runtime reads at start: about 1.1 M for a Python hello, about
147 M for `import sympy`, 2,518 units for the numpy runtime. The hellos in brackets are those
of 0.13.0, pinned by the controller's tests.

| Runtime id | What runs | Typical task: fuel / wall / peak memory |
|---|---|---|
| `python-3.14.6` | CPython 3.14.6, pure Python with the standard library | 0.5 G / 145 ms / 40 MiB (a hello: 122 M) |
| `python-3.14.6-sympy-1.14.0` | the same with sympy 1.14.0 and mpmath 1.4.1 | 3.5 G / 1.6 s / 50 MiB: `import sympy` is most of it (a hello with the import: 3.2 G) |
| `python-3.14.6-numpy-2.5.4` | CPython 3.14.6 with numpy 2.5.4, started from a snapshot with numpy imported | 26 M / 11 ms / 40 MiB (`import numpy`: 24.9 M; a 300×300 `linalg.solve`: about 0.24 G) |
| `quickjs-0.15.1` | QuickJS-NG 0.15.1, JavaScript | 12 M / 1 ms / 1.2 MiB (a hello: 2.4 M) |
| `typescript-sucrase-3.35.1` | TypeScript on the same QuickJS; sucrase strips the types | 0.7 G / 114 ms / 3.4 MiB: loading the transpiler is most of it (a hello: 333 M) |
| `ruby-3.4.1` | Ruby 3.4.1 with the standard library and the gems base64, csv, matrix, prime | 0.63 G / 304 ms / 53 MiB (a hello: 154 M) |
| `lua-5.4.8` | Lua 5.4.8 with its standard library | 2.6 M / under 1 ms / 1.3 MiB (a hello: 0.57 M) |
| `clang-22.1.0` | C17 and C++20 compiled and linked on the node, run as a raw module | the build, under its own budget: 0.2 G for a C hello, 10–20 G and about 5 s for a C++ program of a couple of hundred lines (both measured before 0.13.0); the task: the program's own run and the price of compiling the program (a C hello: 8.3 M) |
| `wasi` | the task's own compiled module | the module's own run and the price of compiling it, 2,048 units a byte at the least (the 224-byte hello: 458,764) |

What an author must know, runtime by runtime. The same facts are in the capabilities
document's `notes`.

- **Python** (`python-3.14.6`):
  - The standard library is present as precompiled bytecode.
  - There are no C extensions (no `_ctypes`, no packages beyond those named).
  - The interpreter's recursion limit applies (a `RecursionError`).
  - Tracebacks show no source line for library frames.
- **sympy** (`python-3.14.6-sympy-1.14.0`):
  - Budget for the import: a hello with `import sympy` costs 3.2 G fuel, and took about 1.6 s on the test fleet.
  - The default 20 G fuel leaves room for real work; set `fuel` higher for heavy simplifications (240 G at most). `wall_ms` changes nothing.
- **numpy** (`python-3.14.6-numpy-2.5.4`):
  - The default integer is 32-bit, so `np.arange(1_000_000).sum()` wraps to 1783293664. Pass `dtype=np.int64` where a total can pass 2^31.
  - There is no BLAS (numpy's own `lapack_lite`), no threads, no SIMD.
  - `np.longdouble` is 128-bit in software, not x86's 80-bit.
  - Floating-point division by zero gives `inf` without a warning; numpy's own errors are raised.
  - Nothing is writable: `np.save` and `np.load` work on `io.BytesIO`.
  - Unseeded randomness follows the task's `seed`.
- **JavaScript** (`quickjs-0.15.1`):
  - Modern JavaScript with QuickJS's `qjs:std` module (stdin through `std.in`; arguments in `scriptArgs`).
  - A recursion too deep is a `RangeError` the program can catch: about 300 levels of plain recursion, about 100 through callbacks such as `map`.
  - A structure nested thousands of levels deep and walked in native code (`JSON.stringify`) can still trap: `RuntimeError`.
- **TypeScript** (`typescript-sucrase-3.35.1`):
  - The types are stripped, not checked: a program with type errors runs.
  - These work: enums, `const enum`, parameter properties, abstract classes, `#private`, `satisfies`, `as const`, generics, top-level `await`.
  - A runtime `namespace` does not.
  - A source that does not parse is a `RuntimeError` naming the line and column.
- **Ruby** (`ruby-3.4.1`):
  - It is a 32-bit build: an Integer above 2^30 is a bignum, so arithmetic on large totals is several times slower than on a desktop.
  - There is no RubyGems at run time; the four gems are on the load path.
  - There is no `bigdecimal`, no sockets, no processes.
- **Lua** (`lua-5.4.8`):
  - The stock interpreter. Errors unwind without WebAssembly exceptions, which costs about 1.4 times the fuel of a native build.
  - `arg`, `...` and `require` of neighbouring files work.
  - A file is found under the exact name it has: `require('LIB.Util')` does not find `lib/util.lua` on any node.
- **C and C++** (`clang-22.1.0`):
  - The language standards are C17 for `.c` and C++20 for `.cc`, `.cpp` and `.cxx`, compiled at `-O2` with wasi-libc and libc++.
  - All the build steps share a budget of 100 G fuel and 1 GiB of memory a step, separate from the program's limits. A build that runs out of that fuel is `CompileError`. A node's 600 s backstop on a build gives no verdict, like the one on a run (section 9).
  - A compile or link error is `CompileError` with clang's diagnostics on stderr. So is a step whose product (an object file, the program) passes 64 MiB, and a build that writes more than 128 MiB in all, its objects and its program together: each is cut at the write.
  - The program the build makes is priced like a module a task brings, and the price comes out of the task's fuel (section 9).
  - A header is found under the exact name it has: `#include "Util.h"` does not find `util.h` on any node.
  - `__TIMESTAMP__` is `Thu Jan  1 00:00:00 1970` on every node.
  - C++ has no exceptions: `try` and `throw` do not compile.
  - The target is 32-bit: `int`, `long` and pointers are 32 bits, `long long` is 64.
  - The program's stack is 8 MiB (a local array of a million ints fits). A small recursive function reaches some 90,000 to 170,000 levels, and how many depends on the node (section 9): a recursion of 20,000 levels returns everywhere, a deeper one should use an explicit stack.
  - `new` past the memory cap is `MemoryExceeded`. A write through a null pointer does not crash: address 0 is memory.
  - The program has no file system.
  - The compiler is compiled once per node process, about 15 s, before its first build.
- **Compiled modules** (`wasi`):
  - A `wasm32-wasip1` command module (`_start`), 8 MiB at most. It is compiled on the node and cached by its hash within a batch.
  - Before it is compiled it is validated and its shape is priced; the price comes out of the task's fuel (section 9).
  - It is refused before running (`CompileError`) when it imports what the sandbox does not provide, or needs a second memory, a 64-bit memory, shared memory or threads, the GC heap, exception handling or stack switching, or nests blocks deeper than 2,048 in one function, or is past another bound of the table in section 9, or has a start function.
  - A module without `_start` is a `RuntimeError`.

## 9. Limits and the sandbox

| Limit | Default | Ceiling | At the edge |
|---|---|---|---|
| Fuel | 20e9 | 240e9 | `Timeout` |
| Memory (one linear memory, 32-bit addresses) | 256 MiB | 2 GiB | the allocator is refused: `MemoryExceeded` |
| stdout, of a task with all its cases | 256 KiB | 8 MiB | the write that passes the cap ends the run: `OutputExceeded` |
| stderr | 64 KiB | 1 MiB | dropped past the cap and flagged (`stderr_truncated`); the run goes on and the verdict is unchanged |
| Call stack of the engine | 8 MiB | | `RuntimeError` (`call stack exhausted`) |
| Tables | 1,048,576 elements, 8 tables | | growth is refused |
| A module a task brings (`wasi`) | 8 MiB | | `CompileError` |
| Blocks nested in one function of such a module, or of a built C/C++ program | 2,048 | | `CompileError` |
| Parameters, and results, of a function type of such a module or program | 128, and 8 | | `CompileError` |
| Values a block of such a module or program takes and gives | 8 | | `CompileError` |
| Locals of one function, and declared by all the functions of such a module or program | 16,384, and 4,194,304 | | `CompileError` |
| Functions of such a module or program | 32,768 | | `CompileError` |
| The body of one function of such a module or program | 128 KiB | | `CompileError` |
| A start function in such a module or program | none | | `CompileError` |
| The product of a C/C++ build step | 64 MiB | | `CompileError` |
| Everything a C/C++ build writes, its objects and its program | 128 MiB | | `CompileError` |
| Cases of a suite | | 1,024 | the taskset is refused |
| Sources of a task | | 1,024 | the taskset is refused |
| Arguments of a task, and of a case | | 1,024 | the taskset is refused |
| Text of a task (sources and their names, arguments, inputs, expected outputs) | | 8 MiB (characters) | the taskset is refused |
| A line of a taskset | | 64 MiB | the taskset is refused |
| Tasks of a taskset | | 4,194,304 | the taskset is refused |

A task's own `limits` override the defaults field by field. Anything above a ceiling is clamped
and named in the preflight's messages. A node clamps again, whatever the package says: a run
cannot get more than the ceilings.

The limits are a run's, and a task with a suite runs once a case. So the most work one task can
ask of a node is its limits times its cases: up to 1,024 runs of up to 240e9 fuel each, and the
task's one stdout cap for all of them. A node refuses a package with a task of more cases,
sources or arguments than the table allows, whoever made the package.

### Fuel and time

- **Fuel is the one budget of a run's work.** `Timeout` means out of fuel, and nothing else. It
  is the same on every node.
- **What costs fuel:** one unit an instruction; 1,000 units a call into the host, whoever
  answers it; and one unit a byte the call moves: a byte read from a file, written to stdout or
  stderr, filled by `random_get`, of a path that is looked up, of a record a wait
  (`poll_oneoff`) reads or writes (48 a subscription, 32 an event), of the arguments or the
  environment each time a program copies them. A byte is the rate the engine charges for a
  guest's own bulk memory copy; the thousand is half a microsecond at the reference rate, about
  what the host's own part of most calls takes. Three file calls take far longer on Windows
  (`path_filestat_get`, `path_open` and `fd_filestat_get`: 45 to 190 µs each where measured),
  so a program that loops on them burns some 7 million units a second there, and the node's
  backstop ends it before its fuel does: no verdict (`NoResult` with reason `wall_clock` if no
  node does better), never a wrong one.
- **The reference rate** is 2,000 million units a second: what a plain busy loop burns on the
  slowest node the fleet has shown. The default budget is ten seconds of such a loop, the
  ceiling two minutes.
- **Fuel bounds work, not time.** How long a unit takes varies with what the program does: a
  loop of additions burns several thousand million a second, a chain of square roots or of
  cache misses a few hundred million.
- **The wall clock gives no verdict.** A node keeps a backstop of its own, sized from the run's
  fuel budget: 10 seconds plus the time the budget lasts at 500 million units a second (a
  quarter of the reference rate). That is 50 s for the default budget and 490 s for the
  largest. A run the backstop ends is a fault of the node, not a verdict: the task is sent out
  once more, and if no node does better it is `NoResult` with reason `wall_clock`.
- **`limits.wall_ms`** (in a task and in the options' default limits) is deprecated: it is
  still accepted and is not a limit. It has no default and no ceiling, and the preflight says so
  with one message. The schema marks it `deprecated`.

### Output

- stdout and stderr are capped at the write: a byte past the cap is never stored, in memory or
  on disk.
- The write that takes stdout past its cap ends the run with `OutputExceeded`, whatever the
  program would have done afterwards.
- stderr past its cap is dropped and flagged, and the run goes on. stderr never fails a task by
  itself.
- The stdout cap is per task: the cases of a suite share it. A case that prints after the cap
  is spent keeps nothing and is `OutputExceeded`.

### A module a task brings, and a program the compiler builds

The node compiles such a module itself, and a compile, once started, cannot be stopped. The
engine's compiler takes time with the square of a function's locals times the depth of its
loops: a module of 6.9 KB took 1.5 s, one of 28 KB took 81 s and 1.5 GiB. So:

- The module is validated, and its shape is priced, before anything is compiled. The price
  comes out of the task's fuel budget, once a task and not once a case.
- The price is 2,048 units a byte of the module; plus 4 units for every unit of (the locals of
  a function)² × (the depth of its nested loops); plus 100 units for every unit of (the targets
  of a branch table) × (the blocks around it).
- Since 0.17.0 the price also has: 4 units for every unit of (the branches of a function)², for
  a function of more than 1,024 branches; 128 units for every unit of (the locals a function
  uses) × (its branches) past 16,384; 4 units for every unit of (the values a block passes) ×
  (the blocks around it), for a block of two values or more; 64 units a local past 256 in a
  function; and a function is priced as 24 bytes at the least. Each is what the engine was
  measured to take for that shape, in the proportion a byte's price has to a byte's time.
- A task whose budget does not hold the price is `Timeout`, and nothing is compiled.
- Two kinds of code compile far more slowly than their price says: calls through a table
  (measured 36 to 68 times the time the price buys at the reference rate), and loads and stores
  on a host with hardware stack protection, where every trap is an explicit check (loads
  measured at up to 52 times). Within every bound and the default budget, a module of many such functions can hold
  one core of a node for minutes while it compiles (8 MB of them took five to seven minutes on
  a fast x64 node). A compile cannot be stopped once it has started. No verdict is wrong because
  of it, and the memory it takes is bounded by the estimate below. This is a stated limit; the
  prices of these kinds are not changed.
- Blocks nested deeper than 2,048 in one function are refused with a `CompileError`. Its stderr
  is `a function of the module nests blocks deeper than 2048`. The shipped interpreters nest
  115 to 781 deep.
- Since 0.17.0 the other bounds of the table above are refused the same way, each with its own
  words on stderr: `a function type of the module has more than 128 parameters`, `... more than
  8 results`, `a block of the module takes and gives more than 8 values`, `a function of the
  module has more than 16384 locals`, `the functions of the module declare more than 4194304
  locals together`, `the module has more than 32768 functions`, `a function of the module is
  larger than 128 KiB`, and `the module has a start function, which a task's module may not
  have: a program starts at _start`. A module whose compile may take more memory than a node
  sets aside for one is refused as well: `compiling the module may take more than
  1536 MiB of memory`.
- The compile uses the calling worker's thread only, so it stays within the cores a batch
  counts.
- The memory the engine takes while it compiles is in no task's memory cap. It is estimated
  from the shape: 9 KiB a function and 6 KiB a byte of the function the compiler works on (more
  for its loads, stores, divisions, conversions that may trap and calls through a table), and
  what the compiler keeps of every function until the module is done, by its instructions: 16
  bytes a unit, an instruction of arithmetic 3 units, a branch 9, a call 11 and one a value it
  passes, a load or a store 40, a division 68, a call through a table 100 and one a value, a
  conversion that may trap 112. The figures are the larger of an x64 node in both trap modes
  and an arm64 node, with a margin of at least 15 %. A module estimated over 1,536 MiB is
  refused before it is compiled. Compiles that may take 64 MiB or more share 1,536 MiB between
  them in a node's process: the largest runs alone, several of ordinary programs side by
  side. A node counts those 1,536 MiB where it decides how many tasks run at once
  (section 11).

The fuel a task reports includes the price: a C hello costs 8,628,668 (8,314,372 before 0.17.0;
it reported 710 before 0.13.0) and the 224-byte raw hello 482,292 (458,764; it reported 6).

What a program can reach:
- **Files:** only its runtime's library, or for Lua and C/C++ its own sources, read-only. It cannot write, create, remove, rename or list. A raw module and a C/C++ program have no file system.
- **Names:** a folder is reached through a table of its exact names. A lookup under another spelling (`Util.h` for `util.h`, a Windows short name, an alternate data stream, a backslash) finds nothing on any node. In a runtime's folder only the pinned files exist.
- **File records:** a file's record says nothing of the host. Its three times and its device read as zero, its file number follows from the guest path alone, and a folder has size zero. So `os.stat(...).st_atime_ns` and `st_ino` are the same on every node.
- **Machine, network, processes, threads:** nothing of the machine; no network, processes or threads.
- **Clock:** the run's own. It starts at one pinned instant and moves only when the program sleeps, and a sleep returns at once. Every clock reports a resolution of 1 ns.
- **Randomness:** a stream seeded by the task. Every seed has a stream of its own. Seed 0, the default, keeps the stream it always had; the stream of every other seed changed in 0.13.0 (seeds 2k and 2k+1 used to share one).
- **Environment:** empty, but for what pins the interpreter.

In a host whose process runs under hardware-enforced stack protection (a .NET 9+ executable on
a Windows machine that enforces shadow stacks), every trap is an explicit check, so a guest's
crash never takes the host down. Elsewhere traps are hardware faults the engine catches, which
is faster. The verdicts, the outputs and the fuel are the same either way: the controller's
tests run the same programs in both modes in one process and compare every field of their
results (`VerifyTrapModeTests`).

One thing does depend on the mode, and on the processor architecture: how deep a recursion
goes before the engine's 8 MiB stack ends. A frame is larger where traps are explicit checks: a
small C function returned from 104,850 levels with hardware traps and from 87,375 with explicit
checks on one machine, a function of a raw module from 174,755 and 131,066. The tests run a
recursion of 20,000 levels (it returns in both modes) and one of 1,000,000 (it overflows in
both). Between those a deep recursion can end as `Pass` on one honest node and as
`RuntimeError` (`call stack exhausted`) on another; when such a task is sampled it is a
dispute. This is a stated limit of this version.

The sandbox runs only on the engine it was built and tested with: the .NET binding the controller
was compiled against, and the native library pinned by its SHA-256 for the platform (section
13). Anything else, an older patch, a damaged copy, another controller's different file in the
client's shared load context, keeps the sandbox off, and the node's self-check names both
versions or both hashes.

## 10. Determinism and integrity

What is guaranteed:
- **The report and the outputs.** They are the same bytes for a taskset wherever and however
  often it runs, as long as every node did its work and told the truth. On the test fleet the
  same report came back across three placements, two fleet sizes and every demo run twice
  (before 0.13.0).
- **A verdict does not depend on the machine.** `Timeout` is out of fuel, which is the same on
  every node. A run a node's clock ended has no verdict.
- **A program sees the same world everywhere:** the same clock, the same random stream, the
  same file names and file records (section 9), no host paths and no directory listings.

What is a metric, not identity (it lives in the provenance):
- **Fuel** is the program's own and reproducible on one host.
  - Since 0.12.0 the budget is given once the module is instantiated, so the copy of its data segments (a unit a byte where wasmtime copies them, nothing where Linux maps an image) is in neither the budget nor the fuel reported. That copy was the fixed step earlier versions showed for Ruby and Lua between Linux and the other systems (a Lua hello before 0.12.0: 569,680 on Linux, 602,449 on Windows and macOS).
  - Since 0.13.0 a call into the host costs fuel by the bytes it moves (section 9), so every task's fuel grew by the bytes its runtime reads at start. Figures quoted before 0.13.0 are lower by that. The hellos then, on Windows x64: `python-3.14.6` 122,363,490 (it was 121,262,306); `python-3.14.6-sympy-1.14.0` with `import sympy` 3,205,340,541 (3,058,460,132); `python-3.14.6-numpy-2.5.4` with `import numpy` 24,920,091 (24,917,573); `quickjs-0.15.1` 2,359,037; `ruby-3.4.1` 153,868,237; `lua-5.4.8` 571,435; `typescript-sucrase-3.35.1` 333,353,693. Those were the figures of 0.13.0 to 0.16.0.
  - Since 0.17.0 a call into the host costs 1,000 units besides its bytes (section 9), so every task's fuel grew by a thousand for every call its runtime makes (a CPython hello makes 118, the import of sympy 7,814). The hellos now, on Windows x64, with the figure of 0.13.0 to 0.16.0 in brackets: `python-3.14.6` 122,481,638 (122,363,490); `python-3.14.6-sympy-1.14.0` 3,213,154,830 (3,205,340,541); `python-3.14.6-numpy-2.5.4` 24,941,299 (24,920,091); `quickjs-0.15.1` 2,367,084 (2,359,037); `ruby-3.4.1` 153,965,549 (153,868,237); `lua-5.4.8` 588,454 (571,435); `typescript-sucrase-3.35.1` 333,420,186 (333,353,693).
  - A task of the `wasi` or the `clang-22.1.0` runtime also reports the price of compiling its module (section 9).
  - The hello of every runtime costs one pinned number on Windows, Linux and macOS (the controller's tests), CPython reading its library from files (`python-3.14.6` and the sympy runtime) included. Up to 0.12.0 the fuel of those two drifted between hosts by a few hundredths of a percent, because the interpreter's imports read the host's file records; since 0.13.0 a file's record says nothing of the host (section 9), and the figures have been the same on the three systems since. The tests also list a CPython hello's calls into the host in order, and the three systems' listings are equal call by call.
- **Peak memory** of a CPython task can differ by one 64 KiB page.
- **Wall time** depends on the hardware.

Before 0.13.0 a program that printed the metadata of its runtime's own files (their times or
file numbers), or C's `__TIMESTAMP__`, disagreed between nodes. A file's record now says nothing
of the host (section 9), and those values are the same on every node.

### What the server does with what nodes return

Anonymous nodes are trusted by checking. A node's word is a claim, and the server takes it in
under rules.

- **Tickets.** Every task package has a ticket: 16 random bytes, made when the package is, sent
  to the node with it and returned in the result. A ticket is never written to a document.
- **What is accepted.** A result package is accepted only under a ticket of that fan-out, when
  its blob is the bytes its reference names, when it was made by the server's own controller
  version, engine and runtime bytes, and when it holds exactly the tasks of the package it
  answers: none missing, none foreign, none twice.
- **A package is walked before it is decoded.** Every text and every list of a frame must lie
  inside the frame's own bytes, every count inside its bound (a result has at most 1,024 case
  records), and every text must be valid Unicode with its true length stated. A package that
  is not is `unreadable`.
- **A result is held to its task.** The server has the task, so a result must be one a run of
  that task could give: fuel from 0 to the task's budget for each of its runs, peak memory
  from 0 to its cap, no time below zero, stdout within the task's cap (all its cases together),
  a case record for each case of its suite in order (or none, for a task that never ran: a
  module that did not compile, a compile its budget did not pay for), a pass with exit code 0,
  and for a task with case records the verdict and the exit code of the first case that did
  not pass. Stderr is held to the ceiling only: it is also where a node puts what the engine
  or the compiler said of a run that never started. A result outside this is `content`.
- **What is dropped.** Anything else is dropped and counted (`rejected_results`), by reason:
  `unknown_ticket`, `duplicate`, `unreadable`, `version`, `content`, `surplus`. A dropped
  package fails that package and not the run, whatever it was that failed while it was read.
- **One controller version a job.** A package names the version that made it, and a node on
  another version refuses it before doing anything.
- **Sampling.** `SamplingRate` runs that share of the packages twice, in the same fan-out as
  the rest. The platform picks the packages at random and keeps the two executions on
  different machines, so a node cannot tell whether it is being checked. With one machine
  available nothing runs twice. A copy that did not come back with a verdict makes no pair and
  is not a mismatch; it does make the fan-out short of its sampling, and the documents say so
  (section 6).
- **Repair.** A task with no verdict from the first fan-out (no accepted answer, or only a
  fault of the node that ran it) is sent out once more. If it still has none it is `NoResult`
  with a `reason`: `wall_clock`, `node_fault` or `no_answer`. The repair round runs under the
  sampling of the first fan-out, and its executions are ruled on the same way: two that agree
  are accepted, two that disagree are a dispute. So a node that leaves packages unanswered
  does not get their tasks back in a round nobody checks. The server does not know which
  packages the platform ran twice, so the repair of a package is sampled at the rate that was
  asked for, whether or not the package was one of them.
- **The server never chooses between two executions.** Executions that agree on verdict, exit
  code, outputs and per-case outcomes are accepted. Executions that disagree are a dispute.
- **Disputes.** A disputed task runs three more times on different machines
  (`Grid.Replicas(3)`). The accepted result is the one returned by the most of those three, at
  least two, with no tie. Otherwise the task is `Disputed`: it is out of the pass counts (it is
  not a pass) and of the products (its outputs line has empty `stdout` and `stderr`).
- **One machine.** A task that was not sampled rests on one machine. The integrity document
  counts those tasks (`single_execution`).

The known limit: the server sees results, not machines. Reading answers as machines rests on
the platform holding a node to one result for each package it was given; the controller cannot
check that itself. Grid 1.3.0 and later hold a node to it: of what a node's batch returns they
keep one value at most for each return variable its tasks asked for. What the controller does
alone, whatever a node returns:

- a node answers only for packages it was given (tickets);
- in the first fan-out, and in a repair round under its sampling, more answers than the two
  asked for send the package's tasks to a dispute;
- in the dispute round, and in a repair round that asked for each package once, a package with
  more answers than were asked for has none of them taken;
- so a node does not win a dispute in which all three copies came back: a second answer of its
  own is one too many, and the task stays `Disputed`.

What rests on the platform:

- where a node is held to one result a package, a node that answers one package of a round
  twice has left another short of its answer. In the dispute round an answer then counts towards
  "two machines" only as far as it cannot be one machine's second: of the answers for the
  leading result, as many are left out as the other packages of the round are short of. If the
  result no longer leads by two, the task stays `Disputed` (outcome `surplus_copies`). With one
  lying node among the copies, a result accepted from a dispute was then returned by an honest
  machine;
- where a node is not held to it (a Grid older than 1.3.0), a second answer can take the place
  of a copy that did not come back, and the controller cannot tell that from two machines
  outvoting a third;
- a package that was not sampled rests on the machine that ran it either way.

One lying node can keep a dispute from being resolved, and so can answers that fail to come
back: in a round that is short of answers (a pool of fewer than three machines, with more than
one package in dispute), nothing is settled by two answers. Two lying nodes that hold two of a
task's three copies are the majority, and win.

## 11. Scale, delivery and the nodes

- **Tasks and results travel as blobs, end to end.**
  - The server cuts the run into packages, about a thousand for a large run when the caps below allow it. Each package is stored as a compressed blob.
  - A node is told which packages are its share and downloads them over the blob channel, in chunks that resume after a break. It uploads each package's results as a blob.
  - Hashes are checked both ways.
  - `Verify.ExecutePackage` is the one way work reaches a node. The single-task activity `Verify.Execute` is removed.
  - An activity's parameters and its return value are references: a blob's id, or a package reference of about 230 bytes (a result's about 145). The one thing in a message that still grows with a run is the list of those references, one a package: `Grid.ForEach` fans out over a collection. It is a small part of a per cent of the taskset, and it is bounded by the taskset's bounds: the most tasks a taskset may have are about 9,200 packages at the default output caps, 2 MB of references. The bound is loose at the far edge (section 14).
- **Nothing that grows with the taskset is read whole into memory.**
  - The server reads a taskset a line at a time. Its tasks wait in working files of the server's own (about the size of the taskset, deleted when the packages are stored) and are stored a package at a time. A preflight keeps nothing.
  - The repair and dispute rounds and the check tasks of generate-and-check are made the same way, a package at a time.
  - The four documents are written to files as they are made and handed out file to blob. The server keeps a small record a task (its index, and for each execution what is compared), never a task's text, its outputs or a document.
- **Measured (0.14.0, the controller's load test, the server and three nodes in one process on a development desktop):** 100,000 tasks of 2 KiB each, a taskset of 202 MiB, a twentieth of the packages run twice.
  - What was live on the managed heap (read after a full collection, four times a second) rose by 2 MiB at the most while the taskset was split and by 45 MiB over the whole run; the heap as it stood, garbage included, by 20 MiB and 65 MiB; the process's working set peaked at 356 MiB. The run took three to five minutes there and under a minute on the build server's Linux machine.
  - 1,562 task packages (0.93 MB of blobs) and 1,641 result packages (1.04 MB).
  - The largest message to a node, its whole share: 547 references, 126,357 bytes. The largest single request: 231 bytes. All three shares together: 379,071 bytes; what the nodes returned: 237,945 bytes.
  - The job's variables: the list of task packages 360,822 bytes, the list of result packages 237,945 bytes, everything else a few words.
  - The documents, as blobs: report 9.4 MB, outputs 15.4 MB, provenance 7.8 MB, integrity 0.6 MB.
- **Caps that are caps.**
  - A batch holds 32 tasks by default, 4,096 at most. It is also cut where its tasks' text reaches 8 MiB (characters) or their output caps together reach 64 MiB. A task counts for twice its stdout cap plus its stderr cap.
  - A package holds at most 4,096 tasks, 8 MiB of task text and 256 MiB of output caps. At the default caps a task counts for 576 KiB, so a batch holds at most 113 such tasks and a package 455: a run of more than about 455,000 tasks at the default caps is cut into more than a thousand packages (a million into about 2,200). Tasks with smaller output caps make larger packages.
  - A single task whose text is over 8 MiB is refused by its index when the taskset is split.
  - A node holds itself to all of it, whatever the package says: limits are clamped to the ceilings on the node, and a package or a batch larger than a node takes is refused whole.
- **Results.**
  - A node writes the results to a file batch by batch and uploads the package from the file.
  - The server reads a result package frame by frame, each within a bound.
- **Refusals.** A node checks a package before it runs any of it. A package made by another controller version, one larger than a node takes, or one whose runtime the node lacks or has other bytes of, is refused whole, and the platform gives it to another node.
- **Transfers.** One attempt of a blob transfer has a time to finish in: two minutes plus the time its bytes take at 32 KiB a second. One that does not finish no longer blocks the node's other transfers.
- **Measured (before 0.13.0):**
  - a million small tasks left the server as 1,116 package blobs, 3 MB in all (at the default output caps the same run is now about 2,200 packages);
  - a node behind an 8 kbit/s link finished its share of a 100,000-task run before the run's last node did.
  - The scale runs on the fleet have not been repeated since 0.13.0 changed the fuel rules and the package caps.
- **One plan, one send.**
  - Each node gets a share by its benchmark rate (the reference batch `verify-task@mixed-v2`: one Python program and one QuickJS program per worker thread).
  - A run therefore ends when its slowest share ends.
  - A node silent for 90 s loses its share, and the share is done again elsewhere from the start.
  - The repair round and the dispute round are fan-outs of their own, and they are empty in a run without lying or failing nodes.
- **On a node:**
  - batches run on dedicated threads below normal priority;
  - at most `min(cores, usable RAM / memory per task, MaxDegree)` run at once, 16 by default. The usable RAM is three quarters of the memory that is free when the batch starts. For C/C++ tasks the compiler's 1 GiB a step counts as the memory per task when it is the larger;
  - compiling a task's own module, or a program built from C/C++, takes the worker's thread only. The first use of a runtime in a process compiles that runtime's own module on the engine's threads, once;
  - a program's output never reaches the disk. Everything else a run writes (a run's input, a task's sources, the compiler's products, a result package until it is uploaded) lives in the client's own folders. A run's and a task's files are deleted in the background soon after it ends, off its own path: a real-time file scanner can hold a delete for a long time. A result package is deleted when it is uploaded.
- **Pool rate.** The six-node test fleet scores about 54 reference tasks per second (`capabilities.pool.rate`), measured before 0.13.0.

## 12. Demo kits

Under `AI/OutWit.Controller.AI.Verify.Live/demo/`, each kit has:
- a `SCRIPT.md` (how to run it, what to look at);
- the inputs;
- a script that turns the saved outputs into a table and checks every verdict against the kit's own expectations;
- an `EXPECTED.md` with the numbers measured on the test fleet.

| Kit | Shape | What it shows |
|---|---|---|
| `01-rlvr` | `VerifyRun` | 10 problems × 7 candidates in Python, JavaScript, Ruby and Lua. The correct ones pass; each kind of mistake gets its verdict: a wrong format, a missed edge case, a crash, a timeout, lost precision. Rewards: 41 of 70 |
| `02-math` | `VerifyGenerateAndCheck` | a synthetic math corpus: derivatives, antiderivatives, roots, simplifications, sums, limits, with flawed generators; 24 items verified, 13 rejected, 1 generator failed |
| `03-wasm` | `VerifyRun` | C and Rust modules a lab compiled itself: correct solutions, the usual mistakes (an `int` total, an `unwrap`, trial division, a missed bound, the case kept), a module asking for the network and one built for threads, both refused before they run |

Short demo tasksets, one per runtime (`tasksets/*-demo.jsonl`), cover ten passing programs, a
wrong answer, a crash and a timeout for each language.

`tasksets/hostile.jsonl` is the hostile taskset of the fleet proofs, with the verdict every
node must give each of its fifteen tasks beside it (`tasksets/hostile.expected.tsv`): a flood
of output from a script and from a module, a module priced above its fuel before it is
compiled and one refused for its nesting, wild pointers handed to the host, loops without end,
a sleep of a hundred seconds, a fill of 250 MiB without end, a program that prints what it can
read about its files, and controls between them. Both files are written by code
(`VerifyHostileKit` in the controller's tests) and the tests run the taskset through the
sandbox and hold every verdict. Three things the kit shows that are easy to expect otherwise:
a script is handed to its interpreter as a string, so there is no `m.py` to open (the library
`/lib/python314.zip`, `/lib` and `/` are there); a sleep returns at once and moves the run's
own clock; a bulk memory fill and a call into the host cost fuel by the bytes they move, so no
task of the kit is ended by a node's wall clock.

## 13. Versions and what a deployment carries

| Part | Version | Where it comes from |
|---|---|---|
| The controller | 0.17.0 | `OutWit.Controller.AI.Verify` (GitHub Packages; nuget.org at 1.0.0) |
| The model and the scripts | 0.4.0 and 0.7.0 | `OutWit.Controller.AI.Verify.Model`, `OutWit.Controller.AI.Verify.Scripts` |
| The engine | wasmtime long-term line 48: the .NET binding 48.0.2 over the native library 48.0.8, pinned by SHA-256 | natives per platform, release `wasmtime-v48.0.8-2` (8 to 10 MB each; the libraries of `wasmtime-v48.0.8`, byte for byte, with their third-party notices) |
| The language runtimes | as in section 8 | `verify-wasm.zip`, release `verify-v0.15.0` (67.5 MB) |
| The numpy runtime | CPython 3.14.6 + numpy 2.5.4 | `verify-python-numpy.zip`, release `verify-v0.15.0` (17.2 MB) |

How a deployment uses them:
- **The server.** Its build fetches the archives by hash and packs one zip per platform for the nodes. A node downloads it once per controller version.
- **One controller version a job.** A task package names the version that made it; a node on another version refuses it, and the server refuses a result made by another version, engine or runtime bytes.
- **The first use of a runtime in a node process** compiles its module (the 75 MB LLVM compiler takes about 15 s). The process keeps the compiled module.
- **Licences** travel with what they are about: every folder of an archive, and the controller's module, has a `THIRD-PARTY-NOTICES.txt` that lists the third-party software in the folder, what is linked statically into its module included, and gives the terms of each part (as a text in the file, or as a licence file beside it that the file names). The files are written by a tool from pinned sources and a job fails when a committed file is not what the tool writes; a test holds every folder to a pinned list of files and every file to its notices. The list of the engine's Rust crates was made by following the engine's manifests, not by asking its compiler, and the Rust standard library's own account of what it took from others is pointed to, not reproduced.
- **Rebuilding:** `verify-assets` rebuilds the runtime set, the files inside the archives, from pinned downloads, byte for byte; the catalog pins every one of those files. The archives themselves are packed the same way every time (a fixed order, fixed times, zlib at level 9), and a job builds each again from its pins at every change and fails when it is not the archive the controller's project file pins by SHA-256 and size; the numpy runtime's archive is built twice, compared, and held to its pin by its own workflow. So an archive a node is sent is what the pinned sources build to, byte for byte, as long as the compressor is zlib; its files are held by their own pins whatever the compressor.

## 14. Known limits

- Not in this wave: PHP (deferred) and Lean (it needs WebAssembly exceptions and gigabytes of library).
- Not offered to a program: network, threads, processes, writable files, a second memory, more than 2 GiB of memory, more than 240e9 units of fuel a run (two minutes of a busy loop at the reference rate).
- The limits are a run's, and a task with a suite runs once a case: one task can ask a node for 1,024 runs at the fuel ceiling (section 9).
- The list of package references a job carries grows with the run, one reference of about 230 bytes a package. At the default output caps the largest taskset is about 2 MB of them. The controller bounds the tasks of a taskset, not its packages: 4,194,304 tasks that each ask for the stdout ceiling would be cut into about 280,000 packages, a list of 64 MB, and are not refused.
- The scale runs on the fleet (a million tasks) were measured before 0.13.0 and have not been repeated since.
- A task that was not sampled rests on one machine (`single_execution` in the integrity document). So does a task that was sent out once more after a node's fault, unless its repair package was one of the share that runs twice.
- The server does not know which packages the platform ran twice. It counts how many came back with two accepted results against how many the rate asks for, and says `short` when fewer did; it cannot say which package lost its second execution.
- The server cannot see which machine returned an answer. A liar can keep a dispute from being resolved (the task stays `Disputed`); it cannot make its answer the accepted one, except when the honest copies of a disputed task were all lost (section 10).
- The bounds and the price of compiling a task's own module are fitted to measurements of particular shapes, not a proof about every module the engine accepts. A compile, once started, cannot be interrupted, and one expensive compile runs at a time in a node's process (section 9). What the engine takes in memory while it compiles is estimated from the module, refused over 1,536 MiB and counted by the node; the estimate holds for the kinds of code that were measured, and the compile itself is not capped. Calls through a table, and loads and stores on a host with hardware stack protection, compile far more slowly than their price says: within every bound and the default budget a module of many such functions can hold one core of a node for minutes while it compiles, and that compile cannot be stopped. No verdict is wrong because of it, and its memory is bounded.
- Fuel bounds work, not time: a run of slow instructions can take four times as long as its budget lasts at the reference rate, and ten seconds more, before a node's backstop ends it (section 9).
- Python runs the standard library, sympy or numpy, but no other package and no C extension. Ruby has four bundled gems and no others.
- TypeScript is not type-checked. C++ has no exceptions. numpy and C/C++ are 32-bit targets.
- A recursion is bounded:
  - in Python by its recursion limit;
  - in JavaScript by a `RangeError` (about 300 levels);
  - in C/C++ and in a raw module by the engine's 8 MiB stack: some 90,000 to 170,000 levels for a small function. How many depends on the node (the trap mode, the processor architecture: section 9), so a recursion deeper than a few tens of thousands of levels can pass on one honest node and overflow on another.
- The peak memory of a CPython task can differ by a page between hosts (section 10). Verdicts and outputs do not.
- A run waits for its slowest share (section 11).
- The controller is proven on the test fleet. Its release to nuget.org and to the live network is the owner's decision.
