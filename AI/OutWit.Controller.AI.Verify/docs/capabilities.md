# Verify: what the controller can do

A reference for building on the Verify controller. It covers:

- how to call it, and what goes in and what comes out;
- which languages run, at what cost and within which limits;
- what is guaranteed about the results;
- where the edges are.

Every number on this page was measured, in the controller's tests or on a test fleet. The
[README](../README.md) explains how things work inside; [safety.md](safety.md) is the same
sandbox told for the people who lend their computers.

Version described: **Verify 0.11.3**. It is proven on a six-node test fleet (Windows, Linux,
macOS) and is not yet on nuget.org or on the live network.

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
| Limits per task | fuel (one unit per instruction) 50e9 by default and 2e12 at most; wall time 10 s / 120 s; memory 256 MiB / 2 GiB; stdout 256 KiB / 8 MiB |
| Determinism | the verdict report and the outputs are the same bytes on every fleet, every placement and every run |
| Integrity | a share of the packages runs twice on two different machines, and the pairs are compared |
| Scale | a million small tasks in 22 min 41 s on six nodes; 100,000 in 2 min 55 s; 10,000 in 23 s |
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
`Verify.ExecutePackage`.

| Script | Inputs | Outputs | Use it for |
|---|---|---|---|
| `VerifyRun` | taskset blob, options | `report`, `outputs`, `provenance`, `integrity` | verdicts of programs (RLVR rewards, code against tests) |
| `VerifyGenerate` | taskset blob, options | `corpus`, `report`, `provenance`, `integrity` | collecting what programs print (a generated corpus) |
| `VerifyGenerateAndCheck` | generators blob, checker blob, options | `report` (the checks), `corpus` (the candidates), `provenance`, `integrity` | generating candidates and checking each with one checker program |
| `VerifyPreflight` | taskset blob, options | `result` | validating a taskset before a run |
| `VerifyRuntimeDiagnostics` | none | `result`, `capabilities` | what the deployment offers: runtimes, limits, the pool |

### The options (`VerifyOptionsData`)

| Field | Default | Meaning |
|---|---|---|
| `DefaultLimits` | the controller's defaults (section 9) | limits for tasks that do not set their own |
| `BatchSize` | 32 (0 means the default) | tasks per batch; a batch shares one compiled runtime module |
| `MaxDegree` | 16 (0 means the default) | at most this many sandboxes at once on a node (never more than its cores or its memory allows) |
| `SamplingRate` | 0 | the share of packages run twice on another machine for integrity, 0 to 1 |
| `ReportFormat` | — | reserved |

### Before a run

`VerifyPreflight` reads the taskset and answers without running anything:
- `WellFormed`, `TaskCount`, `BatchCount`;
- `RuntimeIds` and `UnknownRuntimeIds`;
- `EstimatedInputBytes`;
- `Messages`: line errors and the limits that will be clamped to the ceilings.

`VerifyRuntimeDiagnostics` returns the `capabilities` document (schema
[`verify-capabilities.schema.json`](verify-capabilities.schema.json)). It is what an
application's runtime picker, limit fields and capacity line need:

| Field | What it holds |
|---|---|
| `controller`, `wasmtime` | the versions |
| `runtimes[]` | `id`, `language`, `sha256`, `available`, and `notes`: one sentence per fact an author must know before writing for that runtime (for example numpy's 32-bit default integer) |
| `defaults`, `ceilings` | the limits (section 9) |
| `packages` | how a run is cut: target count, tasks and bytes per package |
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
| `runtime` | yes | a runtime id (section 8); an unknown id gives `RuntimeUnavailable` for that task |
| `entry` | no | the file to run; defaults to the first source |
| `sources` | yes | file name → content. What is done with them depends on the runtime (below) |
| `args` | no | arguments after the entry |
| `stdin` | no | standard input of a run without a suite |
| `seed` | no | the seed of the sandbox's random source; default 0 |
| `suite` | no | test cases: `stdin`, `args` (appended to the task's), `expected_stdout` (exact), `expected_exit` (default 0) |
| `limits` | no | `fuel`, `memory`, `wall_ms`, `stdout_bytes`, `stderr_bytes`; clamped to the ceilings |

How a runtime takes the sources:

| Runtimes | `sources` |
|---|---|
| `python-*`, `quickjs-0.15.1`, `typescript-sucrase-3.35.1`, `ruby-3.4.1` | the entry is the program, passed inline; other files are not available to it |
| `lua-5.4.8`, `clang-22.1.0` | every source is a file in a fresh read-only folder that is the guest's whole file system, so headers and `require`d modules sit beside the entry. Names must be the same file on every system: `/` between folders, no `.` or `..`, no names Windows refuses, no two names that differ only in case. A broken name is the task's `CompileError` |
| `wasi` | the entry is the module's bytes in base64; its name is the guest's `argv[0]` |

A suite passes when every case does. A case passes when the run exits with `expected_exit` and,
if `expected_stdout` is given, prints exactly that. The first failing case sets the task's
verdict.

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
- Every generator that passed and printed something becomes one run of the checker, with that output on stdin and under the generator's index. The check report lines up with the generators.
- A generator that failed or printed nothing is not checked, but is still in the corpus.

The recommended checker contract is to print the verdict and exit 0 (`OK`, or `MISMATCH` and a
reason). A crash of the checker itself then stays distinguishable as a `RuntimeError`.

The math kit (section 12) is a complete example: 38 sympy generators across six families of
problems, and one checker that verifies every answer by another route.

## 6. What comes back

| Output | Schema | Content | Identical across runs and fleets |
|---|---|---|---|
| `report` | [`verify-report.schema.json`](verify-report.schema.json) | `summary` (`total`, `pass`, `pass_rate`, counts per verdict) and `results[]`: `index`, `verdict`, `exit`, `cases[]` (`case`, `passed`, `verdict`) | yes: the run's identity |
| `outputs` / `corpus` | [`verify-corpus.schema.json`](verify-corpus.schema.json) | one JSON line per task: `index`, `verdict`, `exit`, `stdout`, `stderr`, the truncation flags, and per case its `exit` and `stdout` | yes |
| `provenance` | [`verify-provenance.schema.json`](verify-provenance.schema.json) | per batch: platform (`windows-x64`, …), wasmtime and controller versions, runtime and its hash, degree, wall time, fuel; per task: `fuel`, `peak_memory`, `wall_ms` | no: these are metrics (section 10) |
| `integrity` | [`verify-integrity.schema.json`](verify-integrity.schema.json) | `summary` (`sampled`, `compared`, `mismatches`, `mismatch_rate`) and the pairs, with what differs | no: which packages are sampled is random |

A task that ran twice still has one record in the report and in the outputs. The record is
chosen by rule: the lower verdict, then the exit code, then the outputs. The documents never
name a machine.

## 7. Verdicts

| Verdict | Meaning | Typical cause |
|---|---|---|
| `Pass` | the run, or every case, did what was expected | |
| `Fail` | it ran, but a case printed the wrong thing or exited with the wrong code (not a crash) | a wrong answer |
| `CompileError` | the program never ran | a C/C++ compile or link error; a module that does not validate or imports what the sandbox does not provide; a source name that is not portable |
| `RuntimeError` | it crashed, or exited non-zero where 0 was expected | an exception, a panic, `abort`, an out-of-bounds access, a stack overflow |
| `Timeout` | fuel or wall time ran out | an endless or too slow program |
| `MemoryExceeded` | the program's allocator was refused memory | Python's `MemoryError`, Lua's "not enough memory", C++'s `bad_alloc` |
| `OutputExceeded` | stdout passed its cap | printing in a loop |
| `RuntimeUnavailable` | the runtime is unknown here, or failed its pin on the node | a typo in `runtime`, a damaged runtime file |

When a crash is a WebAssembly trap and the program wrote nothing to stderr, the sandbox names
the trap there in fixed words:

| stderr | Cause |
|---|---|
| `wasm trap: unreachable code reached (abort, a failed assertion, a trap instruction)` | `abort()`, a failed `assert`, a Rust panic |
| `wasm trap: out of bounds memory access (a pointer outside the memory, or a stack that overflowed)` | a wild pointer, an index far past an array, a C stack overflow |
| `wasm trap: call stack exhausted` | a recursion deeper than the engine's call stack |
| `wasm trap: integer division by zero`, `integer overflow`, `invalid conversion to integer` | the arithmetic traps |
| `wasm trap: call through a null function pointer`, `call through a function pointer of the wrong type` | a broken indirect call |

A `Timeout` writes nothing to stderr: one node may stop on fuel where another stops on the
clock, and the outputs must not tell them apart.

## 8. Runtimes

Each runtime is a WebAssembly module pinned by SHA-256 in the controller's catalog, and the same
bytes run on every platform. A batch shares one compiled module.

The costs below are medians over the demo tasks on the test fleet (`provenance`). They give
the order of magnitude a planner should expect.

| Runtime id | What runs | Typical task: fuel / wall / peak memory |
|---|---|---|
| `python-3.14.6` | CPython 3.14.6, pure Python with the standard library | 0.5 G / 145 ms / 40 MiB (a hello: 124 M) |
| `python-3.14.6-sympy-1.14.0` | the same with sympy 1.14.0 and mpmath 1.4.1 | 3.5 G / 1.6 s / 50 MiB: `import sympy` is most of it |
| `python-3.14.6-numpy-2.5.4` | CPython 3.14.6 with numpy 2.5.4, started from a snapshot with numpy imported | 39 M / 11 ms / 40 MiB (a 300×300 `linalg.solve`: 0.25 G) |
| `quickjs-0.15.1` | QuickJS-NG 0.15.1, JavaScript | 12 M / 1 ms / 1.2 MiB |
| `typescript-sucrase-3.35.1` | TypeScript on the same QuickJS; sucrase strips the types | 0.7 G / 114 ms / 3.4 MiB: loading the transpiler is most of it |
| `ruby-3.4.1` | Ruby 3.4.1 with the standard library and the gems base64, csv, matrix, prime | 0.63 G / 304 ms / 53 MiB |
| `lua-5.4.8` | Lua 5.4.8 with its standard library | 2.6 M / under 1 ms / 1.3 MiB |
| `clang-22.1.0` | C17 and C++20 compiled and linked on the node, run as a raw module | the build: 0.2 G for a C hello, 10–20 G and about 5 s for a C++ program of a couple of hundred lines; the run: the program's own |
| `wasi` | the task's own compiled module | the module's own (the demo modules: a median of 33 thousand fuel) |

What an author must know, runtime by runtime. The same facts are in the capabilities
document's `notes`.

- **Python** (`python-3.14.6`):
  - The standard library is present as precompiled bytecode.
  - There are no C extensions (no `_ctypes`, no packages beyond those named).
  - The interpreter's recursion limit applies (a `RecursionError`).
  - Tracebacks show no source line for library frames.
- **sympy** (`python-3.14.6-sympy-1.14.0`):
  - Budget for the import: about 3.4 G fuel and 1.6 s per task.
  - The default 50 G fuel and 10 s wall leave room for real work; set `wall_ms` higher for heavy simplifications.
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
- **C and C++** (`clang-22.1.0`):
  - The language standards are C17 for `.c` and C++20 for `.cc`, `.cpp` and `.cxx`, compiled at `-O2` with wasi-libc and libc++.
  - All the build steps share a budget of 100 G fuel (a 600 s backstop, 1 GiB a step), separate from the program's limits. A compile or link error is `CompileError` with clang's diagnostics on stderr.
  - C++ has no exceptions: `try` and `throw` do not compile.
  - The target is 32-bit: `int`, `long` and pointers are 32 bits, `long long` is 64.
  - The program's stack is 8 MiB (a local array of a million ints fits). A small recursive function reaches about 100,000 to 160,000 levels.
  - `new` past the memory cap is `MemoryExceeded`. A write through a null pointer does not crash: address 0 is memory.
  - The program has no file system.
  - The compiler is compiled once per node process, about 15 s, before its first build.
- **Compiled modules** (`wasi`):
  - A `wasm32-wasip1` command module (`_start`). It is compiled on the node and cached by its hash within a batch.
  - It is refused before running (`CompileError`) when it imports what the sandbox does not provide, or needs a second memory, a 64-bit memory, shared memory or threads, the GC heap, exception handling or stack switching.
  - A module without `_start` is a `RuntimeError`.

## 9. Limits and the sandbox

| Limit | Default | Ceiling | At the edge |
|---|---|---|---|
| Fuel (one unit per instruction) | 50e9 | 2e12 | `Timeout` |
| Wall time | 10 s | 120 s | `Timeout` |
| Memory (one linear memory, 32-bit addresses) | 256 MiB | 2 GiB | the allocator is refused: `MemoryExceeded` |
| stdout | 256 KiB | 8 MiB | `OutputExceeded` |
| stderr | 64 KiB | 1 MiB | cut and flagged; the verdict is unchanged |
| Call stack of the engine | 8 MiB | | `RuntimeError` (`call stack exhausted`) |
| Tables | 1,048,576 elements, 8 tables | | growth is refused |

A task's own `limits` override the defaults field by field. Anything above a ceiling is clamped
and named in the preflight's messages.

What a program can reach:
- **Files:** only its runtime's library, or for Lua and C/C++ its own sources, read-only. It cannot write, remove, rename or list. A raw module and a C/C++ program have no file system.
- **Machine, network, processes, threads:** nothing of the machine; no network, processes or threads.
- **Clock:** the run's own. It starts at one pinned instant and moves only when the program sleeps, and a sleep returns at once.
- **Randomness:** a stream seeded by the task.
- **Environment:** empty, but for what pins the interpreter.

In a host with hardware-enforced stack protection (a .NET 9+ executable on Windows), every trap
is an explicit check, so a guest's crash never takes the host down. Elsewhere traps are
hardware faults, which is faster. The verdicts, the outputs and the fuel are the same either
way.

## 10. Determinism and integrity

What is guaranteed:
- **The report and the outputs.** They are the same bytes for a taskset wherever and however
  often it runs. On the test fleet the same report came back across three placements, two
  fleet sizes and every demo run twice.
- **A program sees the same world everywhere:** the same clock, the same random stream, no
  host paths and no directory listings.

What is a metric, not identity (it lives in the provenance):
- **Fuel** is reproducible on one host.
  - Between hosts it drifts slightly for the interpreters that read files: CPython by 0.0002–0.06 %; Ruby and Lua by a fixed step between Linux and the other systems (a Lua hello: 569,680 on Linux, 602,449 on Windows and macOS).
  - QuickJS tasks, C/C++ programs and raw modules are equal everywhere.
- **Peak memory** of a CPython task can differ by one 64 KiB page.
- **Wall time** depends on the hardware.

A program that prints the metadata of its runtime's own files (their times or file numbers)
disagrees between nodes. Nothing else a program can observe does.

Integrity works like this:
- `SamplingRate` runs that share of the packages twice, in the same fan-out as the rest.
- The platform picks the packages at random and keeps the two executions on different machines, so a node cannot tell whether it is being checked.
- The integrity document pairs the executions. It counts how many agree on verdict, exit code, outputs and per-case outcomes: the fields an honest machine reproduces anywhere.
- A copy that did not finish (its node failed and the primary was done elsewhere) is left out of the comparison, not counted as a mismatch.
- With one machine available nothing runs twice.

## 11. Scale, delivery and the nodes

- **Nothing that grows with the taskset travels in a message.**
  - The server cuts the run into packages: at most 4,096 tasks or 8 MB of task text each, about a thousand for a large run. Each package is stored as a compressed blob.
  - A node is told which packages are its share and downloads them over the blob channel, in chunks that resume after a break. It uploads each package's results as a blob.
  - Hashes are checked both ways.
- **Measured:**
  - a million small tasks left the server as 1,116 package blobs, 3 MB in all;
  - a node behind an 8 kbit/s link finished its share of a 100,000-task run before the run's last node did.
- **One plan, one send.**
  - Each node gets a share by its benchmark rate (the reference batch `verify-task@mixed-v2`: one Python program and one QuickJS program per worker thread).
  - A run therefore ends when its slowest share ends.
  - A node silent for 90 s loses its share, and the share is done again elsewhere from the start.
- **On a node:**
  - batches run on dedicated threads below normal priority;
  - at most `min(cores, usable RAM / memory per task, MaxDegree)` run at once, 16 by default;
  - everything a run writes lives in the client's own folders and is gone when the run ends.
- **Pool rate.** The six-node test fleet scores about 54 reference tasks per second (`capabilities.pool.rate`).

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
| The controller | 0.11.3 | `OutWit.Controller.AI.Verify` (GitHub Packages; nuget.org at 1.0.0) |
| The engine | wasmtime long-term line 48: the .NET binding 48.0.2 over the native library 48.0.8 | natives per platform, release `verify-v0.6.0` (about 8 MB each) |
| The language runtimes | as in section 8 | `verify-wasm.zip`, release `verify-v0.10.0` (68.7 MB) |
| The numpy runtime | CPython 3.14.6 + numpy 2.5.4 | `verify-python-numpy.zip`, release `verify-v0.11.0` (17.2 MB) |

How a deployment uses them:
- **The server.** Its build fetches the archives by hash and packs one zip per platform for the nodes. A node downloads it once per controller version.
- **The first use of a runtime in a node process** compiles its module (the 75 MB LLVM compiler takes about 15 s). The process keeps the compiled module.
- **Licences** travel inside each archive.
- **Rebuilding:** `verify-assets` rebuilds every archive from pinned downloads, byte for byte.

## 14. Known limits

- Not in this wave: PHP (deferred) and Lean (it needs WebAssembly exceptions and gigabytes of library).
- Not offered to a program: network, threads, processes, writable files, a second memory, more than 2 GiB of memory, more than 120 s of wall time.
- Python runs the standard library, sympy or numpy, but no other package and no C extension. Ruby has four bundled gems and no others.
- TypeScript is not type-checked. C++ has no exceptions. numpy and C/C++ are 32-bit targets.
- A recursion is bounded:
  - in Python by its recursion limit;
  - in JavaScript by a `RangeError` (about 300 levels);
  - in C/C++ by the 8 MiB stack (100,000 to 160,000 levels for a small function).
- Fuel and memory metrics differ slightly between platforms for the interpreters that read files (section 10). Verdicts and outputs do not.
- A run waits for its slowest share (section 11).
- The controller is proven on the test fleet. Its release to nuget.org and to the live network is the owner's decision.
