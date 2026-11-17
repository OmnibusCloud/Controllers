# Verify: what the controller can do

A reference for building on the Verify controller. It covers:

- how to call it, and what goes in and what comes out;
- which languages run, at what cost and within which limits;
- what is guaranteed about the results;
- where the edges are.

Every number on this page was measured, in the controller's tests or on a test fleet. The
[README](../README.md) explains how things work inside; [safety.md](safety.md) is the same
sandbox told for the people who lend their computers.

Version described: **Verify 0.13.0** (with Verify.Model 0.4.0 and Verify.Scripts 0.6.0), for the
test server. 0.11.3 is proven on a six-node test fleet (Windows, Linux, macOS), and the fleet
figures on this page (scale, medians, pool rate) were measured before 0.13.0. None of these
versions is on nuget.org or on the live network.

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
| Limits per task | fuel (one unit per instruction, and one a byte a call into the host moves) 20e9 by default and 240e9 at most; memory 256 MiB / 2 GiB; stdout 256 KiB / 8 MiB. No wall-time limit: the clock is a node's own backstop |
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
2. the repair round: tasks without a verdict go out once more;
3. the dispute round: tasks whose executions disagree run three more times on different
   machines.

`Verify.Settle` then rules on every task and writes the four documents once, into one ledger
blob; `Verify.Collect`, `Verify.CollectOutputs`, `Verify.Provenance` and `Verify.Integrity`
hand them out from the ledger. The [README](../README.md) shows the scripts as they are.

### The options (`VerifyOptionsData`)

| Field | Default | Meaning |
|---|---|---|
| `DefaultLimits` | the controller's defaults (section 9) | limits for tasks that do not set their own. Its wall time is accepted and is not a limit |
| `BatchSize` | 32 (0 means the default) | tasks per batch, clamped to 4,096; a batch shares one compiled runtime module. A batch is also cut earlier by its tasks' text and output caps (section 11) |
| `MaxDegree` | 16 (0 means the default) | at most this many sandboxes at once on a node (never more than its cores or its memory allows) |
| `SamplingRate` | 0 | the share of packages run twice on another machine for integrity, 0 to 1 |
| `ReportFormat` | — | reserved |

### Before a run

`VerifyPreflight` reads the taskset and answers without running anything:
- `WellFormed`, `TaskCount`, `BatchCount`;
- `RuntimeIds` and `UnknownRuntimeIds`;
- `EstimatedInputBytes`;
- `Messages`: line errors (a source name that is not portable is one, by the name), the limits
  and the batch size that will be clamped, a task whose text is over 8 MiB (by its index), one
  message when the taskset or the options set a wall time, and one for every unknown runtime.

`WellFormed` is false for everything `Verify.Split` would refuse the taskset for: a line that
does not parse, a source name outside the portable set, an unknown runtime id, a task that is
too large, an empty taskset.

`VerifyRuntimeDiagnostics` returns the `capabilities` document (schema
[`verify-capabilities.schema.json`](verify-capabilities.schema.json), `verify-capabilities/2`).
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
| `pool` | the fleet as totals: `nodes`, `measured` (nodes with a benchmark), `workers`, `rate` (reference tasks per second). No node is ever named |

### While it runs

A node reports its share about twice a second:
- first the delivery: `receiving tasks: 12/215 packages, 3.1/14.8 MB`;
- then the work: `51200/220160 tasks`.

The job's progress is these counts. Verdicts come at the end, when the report is written.

## 4. The taskset

JSON lines: one task per line, no enclosing array. Schema:
[`verify-taskset.schema.json`](verify-taskset.schema.json).

| Field | Required | Meaning |
|---|---|---|
| `index` | no | the task's index in every output; defaults to the line's ordinal |
| `runtime` | yes | a runtime id (section 8); a taskset that names an unknown id is refused when it is split |
| `entry` | no | the file to run; defaults to the first source |
| `sources` | yes | file name → content. The names must be portable (below). What is done with the files depends on the runtime (below) |
| `args` | no | arguments after the entry |
| `stdin` | no | standard input of a run without a suite |
| `seed` | no | the seed of the sandbox's random source; default 0 |
| `suite` | no | test cases: `stdin`, `args` (appended to the task's), `expected_stdout` (exact), `expected_exit` (default 0) |
| `limits` | no | `fuel`, `memory`, `stdout_bytes`, `stderr_bytes`; clamped to the ceilings. `wall_ms` is still accepted and is not a limit (section 9) |

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
- **The checker spec** (schema [`verify-checker.schema.json`](verify-checker.schema.json)): one task, without an index or a suite, plus `expected_stdout` and `expected_exit`.

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
| `report` | [`verify-report.schema.json`](verify-report.schema.json), `verify-report/2` | `summary` (`total`, `pass`, `pass_rate`, counts per verdict, `rejected_results`, `disputes`, `disputes_resolved`) and `results[]`: `index`, `verdict`, `exit`, `reason` (only for `NoResult`), `cases[]` (`case`, `passed`, `verdict`) | yes, as long as every node did its work and told the truth: the run's identity |
| `outputs` / `corpus` | [`verify-corpus.schema.json`](verify-corpus.schema.json) | one JSON line per task: `index`, `verdict`, `exit`, `reason` (only for `NoResult`), `stdout`, `stderr`, the truncation flags, and per case its `exit` and `stdout` | yes, on the same condition |
| `provenance` | [`verify-provenance.schema.json`](verify-provenance.schema.json), `verify-provenance/2` | per accepted batch: `round` (`main`, `repair`, `dispute`), platform (`windows-x64`, …), wasmtime and controller versions, runtime and its hash, fuel; per task: `fuel`, `peak_memory`, `wall_ms`, and `fault` for a task the node could not run | no: these are metrics (section 10) |
| `integrity` | [`verify-integrity.schema.json`](verify-integrity.schema.json), `verify-integrity/2` | `summary` (the counts below), the `pairs` with what differs, and the `disputes` with how each ended | no: which packages are sampled is random |

What the documents say of a run that did not go well:

- **A task without a result** is `NoResult` with a `reason`: `wall_clock` (a node's backstop
  ended the run), `node_fault` (a node could not run it) or `no_answer` (nothing the server
  accepts came back). Its outputs line has empty `stdout` and `stderr`.
- **A task whose executions were not reconciled** is `Disputed`. It is not a pass, and its
  outputs line has empty `stdout` and `stderr`.
- **`pass_rate`** is `pass / total`: both kinds are in the total.
- **The report's `rejected_results`, `disputes` and `disputes_resolved`** are zero in a run
  without lying or failing nodes.

The integrity summary counts: `tasks`; `sampled`, `compared`, `mismatches`, `mismatch_rate`
(the pairs of the first fan-out); `agreed` (tasks accepted on two or more executions that
agree) and `single_execution` (tasks accepted on one execution, which rest on one machine: a
task that was not sampled is one of them); `disputes`, `disputes_resolved`, `disputed` (the
disputes that stand); `repairs` (tasks sent out once more);
`no_result`; `rejected_results`, and `rejected` by reason (`unknown_ticket`, `duplicate`,
`unreadable`, `version`, `content`, `surplus`).

For a task with a suite, the task's own `stdout` is the last case's.

A task has one record in the report and in the outputs, however often it ran. The server never
chooses between two executions: it accepts executions that agree, and has those that disagree
run again (section 10).

The documents never name a machine. The provenance has no degree and no wall time of a batch:
with the memory cap and the degree the lab's to choose, a node's cores and memory could be read
off them. What a node says of its own trouble reaches a document only as one of the reason
codes above; a path, an account name or the text of an error stays in the node's own log. The
program's own stdout and stderr are the program's and stay as they are.

## 7. Verdicts

| Verdict | Meaning | Typical cause |
|---|---|---|
| `Pass` | the run, or every case, did what was expected | |
| `Fail` | it ran, but a case printed the wrong thing or exited with the wrong code (not a crash) | a wrong answer |
| `CompileError` | the program never ran | a C/C++ compile or link error, or a build whose product is over 64 MiB; a module that does not validate or imports what the sandbox does not provide; a raw module over 8 MiB; a function that nests blocks deeper than 2,048 |
| `RuntimeError` | it crashed, or exited non-zero where 0 was expected (any status, 126 and more included) | an exception, a panic, `abort`, an out-of-bounds access, a stack overflow, `exit(200)` |
| `Timeout` | the fuel ran out, and nothing else. It is the same on every node | an endless or too slow program; a module whose compile the budget does not hold (section 9) |
| `MemoryExceeded` | the program's allocator was refused memory | Python's `MemoryError`, Lua's "not enough memory", C++'s `bad_alloc` |
| `OutputExceeded` | stdout passed its cap; the write that passed it ended the run | printing in a loop; a suite whose cases together print more than the task's cap |
| `NoResult` | no node gave a verdict, also when the task was sent out once more. It says nothing about the program. The documents carry a `reason` | `wall_clock`: a node's backstop ended the run; `node_fault`: a node could not run it; `no_answer`: nothing the server accepts came back |
| `Disputed` | the executions disagreed, and three more on different machines did not give one answer from at least two of them. Not a pass; no outputs | copies that keep disagreeing; copies of the dispute round that did not come back; a package of the dispute round with more answers than the round's packages commonly have (section 10) |
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
  - A compile or link error is `CompileError` with clang's diagnostics on stderr. So is a step whose product (an object file, the program) passes 64 MiB: it is cut at the write.
  - The program the build makes is priced like a module a task brings, and the price comes out of the task's fuel (section 9).
  - A header is found under the exact name it has: `#include "Util.h"` does not find `util.h` on any node.
  - `__TIMESTAMP__` is `Thu Jan  1 00:00:00 1970` on every node.
  - C++ has no exceptions: `try` and `throw` do not compile.
  - The target is 32-bit: `int`, `long` and pointers are 32 bits, `long long` is 64.
  - The program's stack is 8 MiB (a local array of a million ints fits). A small recursive function reaches about 100,000 to 160,000 levels.
  - `new` past the memory cap is `MemoryExceeded`. A write through a null pointer does not crash: address 0 is memory.
  - The program has no file system.
  - The compiler is compiled once per node process, about 15 s, before its first build.
- **Compiled modules** (`wasi`):
  - A `wasm32-wasip1` command module (`_start`), 8 MiB at most. It is compiled on the node and cached by its hash within a batch.
  - Before it is compiled it is validated and its shape is priced; the price comes out of the task's fuel (section 9).
  - It is refused before running (`CompileError`) when it imports what the sandbox does not provide, or needs a second memory, a 64-bit memory, shared memory or threads, the GC heap, exception handling or stack switching, or nests blocks deeper than 2,048 in one function.
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
| The product of a C/C++ build step | 64 MiB | | `CompileError` |

A task's own `limits` override the defaults field by field. Anything above a ceiling is clamped
and named in the preflight's messages. A node clamps again, whatever the package says: a lab
cannot get more than the ceilings.

### Fuel and time

- **Fuel is the one budget of a run's work.** `Timeout` means out of fuel, and nothing else. It
  is the same on every node.
- **What costs fuel:** one unit an instruction, and one unit a byte a call into the host moves:
  a byte read from a file, written to stdout or stderr, filled by `random_get`, or of a path
  that is looked up. It is the rate the engine charges for a guest's own bulk memory copy.
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
- **`limits.wall_ms`** (in a task and in the options' default limits) is still accepted and is
  not a limit. It has no default and no ceiling, and the preflight says so with one message.

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
- A task whose budget does not hold the price is `Timeout`, and nothing is compiled.
- Blocks nested deeper than 2,048 in one function are refused with a `CompileError`. Its stderr
  is `a function of the module nests blocks deeper than 2048`. The shipped interpreters nest
  115 to 781 deep.
- The compile uses the calling worker's thread only, so it stays within the cores a batch
  counts.

The fuel a task reports includes the price: a C hello costs 8,314,372 (it reported 710 before
0.13.0) and the 224-byte raw hello 458,764 (it reported 6).

What a program can reach:
- **Files:** only its runtime's library, or for Lua and C/C++ its own sources, read-only. It cannot write, create, remove, rename or list. A raw module and a C/C++ program have no file system.
- **Names:** a folder is reached through a table of its exact names. A lookup under another spelling (`Util.h` for `util.h`, a Windows short name, an alternate data stream, a backslash) finds nothing on any node. In a runtime's folder only the pinned files exist.
- **File records:** a file's record says nothing of the host. Its three times and its device read as zero, its file number follows from the guest path alone, and a folder has size zero. So `os.stat(...).st_atime_ns` and `st_ino` are the same on every node.
- **Machine, network, processes, threads:** nothing of the machine; no network, processes or threads.
- **Clock:** the run's own. It starts at one pinned instant and moves only when the program sleeps, and a sleep returns at once. Every clock reports a resolution of 1 ns.
- **Randomness:** a stream seeded by the task. Every seed has a stream of its own. Seed 0, the default, keeps the stream it always had; the stream of every other seed changed in 0.13.0 (seeds 2k and 2k+1 used to share one).
- **Environment:** empty, but for what pins the interpreter.

In a host with hardware-enforced stack protection (a .NET 9+ executable on Windows), every trap
is an explicit check, so a guest's crash never takes the host down. Elsewhere traps are
hardware faults, which is faster. The verdicts, the outputs and the fuel are the same either
way.

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
  - Since 0.13.0 a call into the host costs fuel by the bytes it moves (section 9), so every task's fuel grew by the bytes its runtime reads at start. Figures quoted before 0.13.0 are lower by that. The hellos now, on Windows x64: `python-3.14.6` 122,363,490 (it was 121,262,306); `python-3.14.6-sympy-1.14.0` with `import sympy` 3,205,340,541 (3,058,460,132); `python-3.14.6-numpy-2.5.4` with `import numpy` 24,920,091 (24,917,573); `quickjs-0.15.1` 2,359,037; `ruby-3.4.1` 153,868,237; `lua-5.4.8` 571,435; `typescript-sucrase-3.35.1` 333,353,693.
  - A task of the `wasi` or the `clang-22.1.0` runtime also reports the price of compiling its module (section 9).
  - The hello of every runtime costs one pinned number on Windows, Linux and macOS (the controller's tests), but for CPython reading its library from files (`python-3.14.6` and the sympy runtime): the tests allow it to drift between hosts by up to 0.06 %, in how each host answers its file queries. The numpy runtime starts from a snapshot and does not drift.
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
- **What is dropped.** Anything else is dropped and counted (`rejected_results`), by reason:
  `unknown_ticket`, `duplicate`, `unreadable`, `version`, `content`, `surplus`. A dropped
  package fails that package and not the run.
- **One controller version a job.** A package names the version that made it, and a node on
  another version refuses it before doing anything.
- **Sampling.** `SamplingRate` runs that share of the packages twice, in the same fan-out as
  the rest. The platform picks the packages at random and keeps the two executions on
  different machines, so a node cannot tell whether it is being checked. With one machine
  available nothing runs twice. A copy that did not come back with a verdict makes no pair and
  is not a mismatch.
- **Repair.** A task with no verdict from the first fan-out (no accepted answer, or only a
  fault of the node that ran it) is sent out once more. If it still has none it is `NoResult`
  with a `reason`: `wall_clock`, `node_fault` or `no_answer`.
- **The server never chooses between two executions.** Executions that agree on verdict, exit
  code, outputs and per-case outcomes are accepted. Executions that disagree are a dispute.
- **Disputes.** A disputed task runs three more times on different machines
  (`Grid.Replicas(3)`). The accepted result is the one returned by the most of those three, at
  least two, with no tie. Otherwise the task is `Disputed`: it is out of the pass counts (it is
  not a pass) and of the products (its outputs line has empty `stdout` and `stderr`).
- **One machine.** A task that was not sampled rests on one machine. The integrity document
  counts those tasks (`single_execution`).

The known limit: the server cannot see which machine returned an answer. A node that holds two
packages of one fan-out can answer one of them twice and leave the other unanswered. What the
server does about it:

- in the first fan-out, more answers than the two asked for send the package's tasks to a
  dispute;
- in the dispute round, a package with more answers than the round's packages commonly have is
  not settled by counting: its tasks stay `Disputed` (outcome `surplus_copies`);
- a starved package is repaired, or its tasks are reported as `NoResult`.

So a liar can keep a dispute from being resolved. It cannot make its answer the accepted one,
except when the honest copies of a disputed task were all lost.

## 11. Scale, delivery and the nodes

- **Nothing that grows with the taskset travels in a message.**
  - The server cuts the run into packages, about a thousand for a large run when the caps below allow it. Each package is stored as a compressed blob.
  - A node is told which packages are its share and downloads them over the blob channel, in chunks that resume after a break. It uploads each package's results as a blob.
  - Hashes are checked both ways.
  - `Verify.ExecutePackage` is the one way work reaches a node. The single-task activity `Verify.Execute` is removed.
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
- **One plan, one send.**
  - Each node gets a share by its benchmark rate (the reference batch `verify-task@mixed-v2`: one Python program and one QuickJS program per worker thread).
  - A run therefore ends when its slowest share ends.
  - A node silent for 90 s loses its share, and the share is done again elsewhere from the start.
  - The repair round and the dispute round are fan-outs of their own, and they are empty in a run without lying or failing nodes.
- **On a node:**
  - batches run on dedicated threads below normal priority;
  - at most `min(cores, usable RAM / memory per task, MaxDegree)` run at once, 16 by default. The usable RAM is three quarters of the memory that is free when the batch starts. For C/C++ tasks the compiler's 1 GiB a step counts as the memory per task when it is the larger;
  - compiling a task's own module, or a program built from C/C++, takes the worker's thread only. The first use of a runtime in a process compiles that runtime's own module on the engine's threads, once;
  - a program's output never reaches the disk. Everything else a run writes (a run's input, a task's sources, the compiler's products, a result package until it is uploaded) lives in the client's own folders and is deleted when its task or its package is done.
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

## 13. Versions and what a deployment carries

| Part | Version | Where it comes from |
|---|---|---|
| The controller | 0.13.0 | `OutWit.Controller.AI.Verify` (GitHub Packages; nuget.org at 1.0.0) |
| The model and the scripts | 0.4.0 and 0.6.0 | `OutWit.Controller.AI.Verify.Model`, `OutWit.Controller.AI.Verify.Scripts` |
| The engine | wasmtime long-term line 48: the .NET binding 48.0.2 over the native library 48.0.8, pinned by SHA-256 | natives per platform, release `wasmtime-v48.0.8` (about 8 MB each; the bytes `verify-v0.6.0` carried) |
| The language runtimes | as in section 8 | `verify-wasm.zip`, release `verify-v0.10.0` (68.7 MB) |
| The numpy runtime | CPython 3.14.6 + numpy 2.5.4 | `verify-python-numpy.zip`, release `verify-v0.11.0` (17.2 MB) |

How a deployment uses them:
- **The server.** Its build fetches the archives by hash and packs one zip per platform for the nodes. A node downloads it once per controller version.
- **One controller version a job.** A task package names the version that made it; a node on another version refuses it, and the server refuses a result made by another version, engine or runtime bytes.
- **The first use of a runtime in a node process** compiles its module (the 75 MB LLVM compiler takes about 15 s). The process keeps the compiled module.
- **Licences** travel inside each archive.
- **Rebuilding:** `verify-assets` rebuilds every archive from pinned downloads, byte for byte.

## 14. Known limits

- Not in this wave: PHP (deferred) and Lean (it needs WebAssembly exceptions and gigabytes of library).
- Not offered to a program: network, threads, processes, writable files, a second memory, more than 2 GiB of memory, more than 240e9 units of fuel (two minutes of a busy loop at the reference rate).
- A task that was not sampled rests on one machine (`single_execution` in the integrity document).
- The server cannot see which machine returned an answer. A liar can keep a dispute from being resolved (the task stays `Disputed`); it cannot make its answer the accepted one, except when the honest copies of a disputed task were all lost (section 10).
- The price of compiling a task's own module is an estimate fitted to measurements, not a proof about every module. A compile, once started, cannot be interrupted, and one expensive compile runs at a time in a node's process (section 9).
- Fuel bounds work, not time: a run of slow instructions can take four times as long as its budget lasts at the reference rate, and ten seconds more, before a node's backstop ends it (section 9).
- Python runs the standard library, sympy or numpy, but no other package and no C extension. Ruby has four bundled gems and no others.
- TypeScript is not type-checked. C++ has no exceptions. numpy and C/C++ are 32-bit targets.
- A recursion is bounded:
  - in Python by its recursion limit;
  - in JavaScript by a `RangeError` (about 300 levels);
  - in C/C++ by the 8 MiB stack (100,000 to 160,000 levels for a small function).
- The fuel of a CPython task (not numpy) can differ by up to 0.06 % between hosts, and its peak memory by a page (section 10). Verdicts and outputs do not.
- A run waits for its slowest share (section 11).
- The controller is proven on the test fleet. Its release to nuget.org and to the live network is the owner's decision.
