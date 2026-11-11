# OutWit.Controller.AI.Verify

Sandboxed, deterministic, resource-metered execution of untrusted programs
against specifications — on anonymous volunteer nodes. One primitive
(`Verify.ExecutePackage`) carries several products: code-vs-tests verification
(RLVR-style reward checking), code-corpus generation with checking, math
problem generation with CAS checking, and formal proof checking — generation
and verification are the same activity with different programs.

**Status: 0.2.0 — proven on a four-node test fleet.** `VerifyRun` returns a
byte-identical verdict report for a taskset across runs; every node executes
batches at a bounded degree below normal priority, reports its progress inside
a chunk, and attaches its provenance; `VerifyGenerateAndCheck` runs the
generate-then-check shape end to end.

Isolation targets WASM (wasmtime): deterministic, cross-platform
(Windows / Linux / macOS, x64 / arm64), no admin rights or Docker required on
contributor machines, no ambient authority — network and filesystem do not
exist inside the sandbox unless explicitly virtualized in. Verdicts are
verifiable by construction: code passes its test suite or it does not; a
proof checks or it does not.

## Activities

| Activity | Where | Role |
|---|---|---|
| `Verify.Split` | host | taskset (JSON lines) + options → batches, grouped by runtime, limits clamped to the host ceilings; the batches are grouped into packages, each stored as a blob, and what the activity returns is the references |
| `Verify.ExecutePackage` | node | the primitive: the node downloads the package blob and runs its batches — one compiled runtime module per batch, the tasks executed concurrently in isolated stores under fuel, memory and wall-clock limits — then uploads the results as a blob; progress of the delivery and of the work; batch provenance |
| `Verify.Execute` | node | one task given inline, for diagnostics; a taskset never travels this way |
| `Verify.Collect` | host | result packages → the verdict report (aggregates + one record per task; the run's identity, byte-identical regardless of placement) |
| `Verify.CollectOutputs` | host | result packages → the corpus (one JSON line per task with its outputs; byte-deterministic) |
| `Verify.MakeCheckTasks` | host | generator results + a checker spec → check task packages, one check per passing candidate under the candidate's index |
| `Verify.Provenance` | host | result packages → the provenance document (which tasks ran together, on which platform, with which wasmtime and runtime module, at which degree, and what each task cost: fuel, peak memory, wall time; never which machine) |
| `Verify.SamplingRate` | host | options → `SamplingRate` clamped to 0..1, the share of the packages `Grid.Replicas` runs twice |
| `Verify.Integrity` | host | the result packages of all executions → the integrity document (the two executions of each task that ran twice, agreement on verdict, exit, outputs and cases; the mismatch rate) |
| `Verify.Preflight` | host | counts, runtimes referenced, unknown runtimes, size estimate, ceiling notes — before anything runs |
| `Verify.RuntimeDiagnostics` | host | wasmtime version (the native library is loaded to prove it), runtime target, per-runtime availability and hash — of the server's module; a node's module is exercised by `Verify.ExecutePackage` and described by the provenance |

## Scripts

`OutWit.Controller.AI.Verify.Scripts` bundles:

| Script | Shape | Returns |
|---|---|---|
| `VerifyRun(Blob taskset, VerifyOptions opts)` | Split → ForEach ⇒ ExecutePackage (a share of the packages twice) → Collect, Provenance, Integrity | `report`, `provenance`, `integrity` |
| `VerifyGenerate(Blob taskset, VerifyOptions opts)` | the same fan-out; the programs are generators | `corpus`, `report`, `provenance`, `integrity` |
| `VerifyGenerateAndCheck(Blob generators, Blob checker, VerifyOptions opts)` | generators fan out → MakeCheckTasks → checks fan out (a share of the check packages twice) → Collect | `report` (the checks), `corpus` (the candidates), `provenance`, `integrity` |
| `VerifyPreflight(Blob taskset, VerifyOptions opts)` | one host activity | `result` |
| `VerifyRuntimeDiagnostics()` | one host activity | `result` |

`VerifyOptions`: `DefaultLimits`, `BatchSize` (tasks per batch, 0 = 32),
`MaxDegree` (concurrent sandboxes per node, 0 = 16), `SamplingRate` (the share
of packages executed twice for integrity, 0–1; 0 = none), `ReportFormat` (reserved).

## How tasks and results travel

Nothing that grows with the taskset travels inside a message. `Verify.Split`
groups the batches into packages and stores each package as a blob; the
collection `Grid.ForEach` places on nodes is a list of references (blob id,
hash, size, task count). A node is told which packages are its share, downloads
exactly those over the blob channel — in chunks, resumed after a break — and
returns each package's results as a blob of its own; the host reads the result
blobs when it writes the documents. The message that assigns a node its share
of a million tasks is a few hundred references.

A package is one batch while a run is small, so the split between nodes stays
as fine as the batches. A large run is cut into about a thousand packages
(at most 4,096 tasks or 8 MB of task text each), which keeps the number of
transfers bounded while the shares stay proportional to the nodes' rates. A
package blob is Brotli-compressed; a node checks its hash before it reads the
tasks, and the host checks a result blob's hash before it reads the results.

## Documents — authoring without the SDK

Every input and output is a JSON document with a published schema in
[`docs/`](docs/), so a pipeline in any language can author tasksets and read
results; the SDK only moves blobs and submits scripts.

| Document | Schema | Direction |
|---|---|---|
| taskset (JSON lines, one task per line) | [`verify-taskset.schema.json`](docs/verify-taskset.schema.json) | in |
| checker spec (one object) | [`verify-checker.schema.json`](docs/verify-checker.schema.json) | in |
| verdict report | [`verify-report.schema.json`](docs/verify-report.schema.json) | out |
| corpus (JSON lines) | [`verify-corpus.schema.json`](docs/verify-corpus.schema.json) | out |
| provenance | [`verify-provenance.schema.json`](docs/verify-provenance.schema.json) | out |
| integrity | [`verify-integrity.schema.json`](docs/verify-integrity.schema.json) | out |

A minimal taskset — one code-vs-tests task and one plain run:

```json
{"index":0,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"a=int(input());b=int(input());print(a+b)"},"suite":[{"stdin":"2\n3\n","expected_stdout":"5\n","expected_exit":0}]}
{"index":1,"runtime":"quickjs-0.15.1","entry":"m.js","sources":{"m.js":"console.log(6*7)"}}
```

Verdicts: `Pass`, `Fail` (ran, wrong answer or missing signal), `CompileError`,
`RuntimeError` (crashed, or exited non-zero where 0 was expected), `Timeout`
(fuel or wall clock), `MemoryExceeded`, `OutputExceeded`, `RuntimeUnavailable`.
A suite case passes when the run completes with its `expected_exit` (whatever
it is) and, if given, its exact `expected_stdout`.

Generate-and-check: the generators are an ordinary taskset whose outputs are
the candidates; the checker spec is one task line plus `expected_stdout` /
`expected_exit`. Every candidate that passed generation with output becomes
one run of the checker with the candidate on stdin, under the candidate's
index, so the check report lines up with the generators; candidates that
produced nothing usable are skipped (and still appear in the corpus). The
recommended checker contract: print the verdict and exit 0, so that a crash
of the checker itself stays distinguishable as a `RuntimeError`.

## Integrity

Anonymous nodes are trusted by checking, not by assumption. `SamplingRate` runs
a share of the packages twice, in the same `Grid.ForEach` call as the rest. The
platform picks the packages at random and keeps the two executions on different
machines, so a node cannot tell whether its package is being checked. The
integrity document pairs the two executions of each task and says whether they
agree on verdict, exit code, outputs and per-case outcomes —
exactly the fields an honest execution reproduces anywhere. With a single machine
available nothing runs twice. The mismatch rate is over the compared pairs. The
report and the corpus have one record per task, chosen by rule: the lowest
verdict, exit code, then outputs.

## Determinism

The verdict report is the run's identity: it carries verdicts, exit codes and
per-case outcomes — the fields two honest executions agree on wherever they
ran — and is byte-identical for a taskset regardless of which nodes ran which
packages (proven on the test fleet across three placements). Fuel is
reproducible per host but drifts between hosts for CPython tasks — by
0.0002–0.06 % with the bytecode layers (the remaining difference is in how
each host's WASI answers the interpreter's stat and descriptor queries), while
QuickJS tasks and raw WASI modules are byte-equal everywhere — and the peak
linear memory of a CPython task can differ by one 64 KiB page with it; wall
time is hardware. All three are metrics and live per task in the provenance
document. The sandbox pins the clock and seeds the random source from the
task, so a program sees the same world on every node.

## Node behaviour

A worker is often a desktop. A batch runs on dedicated threads below normal
priority (Windows honours it; Linux ignores thread priorities, where the
degree cap is the protection) at `min(cores, usable RAM / per-task memory cap,
MaxDegree)` — 16 by default. Every result batch carries its provenance: runtime
target, wasmtime and native library origin, runtime module hash, degree, task
count, wall time.

A node starts every package of its share together. The package blobs download
ahead of the work, four at a time, so the share is on the node's disk early and
the rest of the run needs the link only for progress and results; one package
executes at a time, since a batch already takes the node's cores. A download or
an upload that fails is tried again for about two minutes before the package
fails, on top of the blob channel's own retries.

The node reports its share, not one batch: first the delivery (`receiving
tasks: 12/215 packages, 3.1/14.8 MB; 0/220160 tasks done`), then the work
(`51200/220160 tasks`), about twice a second through the host's activity
progress sink. The fraction is tasks done out of the tasks of the node's share.

The node benchmark (`verify-task@mixed-v2`) is the throughput of a fixed
reference batch — one CPU-bound Python program and one QuickJS program, four
per worker thread, with their expected outputs — as tasks per second of the
median run at the node's degree; a node that computes wrong cannot score, a
node without runtimes reports the default (unranked) score. `Grid.ForEach`
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
| `quickjs-0.15.1` | QuickJS-NG 0.15.1, JavaScript | the source, inline |
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

`OutWit.Controller.AI.Verify.Assets` (`verify-assets build <dir>`) rebuilds
the whole set from pinned downloads, byte for byte, and prints the hashes the
catalog pins.

The raw `wasi` runtime has no module of its own: each task carries one,
compiled by the lab offline, and the sandbox compiles it on the node (cached by
content hash within a batch). args, stdin, suite and limits work as for any
task; a module that does not validate or cannot be instantiated (an import the
sandbox does not provide) is a `CompileError`, one without `_start` a
`RuntimeError`. A 224-byte hello, assembled by hand in the tests:

```json
{"runtime":"wasi","entry":"hello.wasm","sources":{"hello.wasm":"AGFzbQEAAAABEANgBH9/f38Bf2ABfwBgAAACZwMWd2FzaV9zbmFwc2hvdF9wcmV2aWV3MQhmZF93cml0ZQAAFndhc2lfc25hcHNob3RfcHJldmlldzEHZmRfcmVhZAAAFndhc2lfc25hcHNob3RfcHJldmlldzEJcHJvY19leGl0AAEDAgECBQMBAAEHEwIGbWVtb3J5AgAGX3N0YXJ0AAMKDwENAEEBQQBBAUEIEAAaCwssAQBBAAsmIAAAAAYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABoZWxsbwo="},"suite":[{"expected_stdout":"hello\n"}]}
```

Honest v1 scope: pure Python with the standard library, JavaScript, and
compiled WASI modules. No C extensions inside CPython-WASI yet.

## What the module carries

This is the first controller that calls a native library in-process (wasmtime,
through the `Wasmtime.Dotnet` binding), so the deployed module carries the
library per platform in the .NET layout its `deps.json` describes:

```
verify.module/
  OutWit.Controller.AI.Verify.dll, .deps.json, controller.json
  OutWit.Controller.AI.Verify.Model.dll
  Wasmtime.Dotnet.dll                      staged by the build
  runtimes/<rid>/native/                   wasmtime, one data asset per RID
  wasm/                                    the language runtime modules, one data asset
```

Two load modes meet this layout. An isolated plugin load context (the server,
the module-load test) resolves the library through the module's `deps.json`.
The cloud client loads controllers into the shared load context, where that
file is never read and a P/Invoke probes only the application folder and the
module root — so the controller registers `VerifyNativeLibrary` as the
binding's DllImport resolver before its first wasmtime call, and it loads
`runtimes/<rid>/native/` from the module itself (the host's runtime
identifier first, then the portable one).

The natives and the runtime modules are Tier-2 data assets (`ControllerDataAsset`),
fetched by consumers at build time from the `verify-v<version>` release of this
repository and verified by hash; the nupkg itself stays small. Licences travel
inside the asset archives (wasmtime: Apache-2.0; CPython: PSF; QuickJS-NG: MIT).

`VerifySelfCheck.Run()` reports, from inside a loaded module, where the binding
and the runtimes resolved from and whether one task per runtime ran — the
module-load test calls it through the real isolated loader, and an operator
can call it as a doctor-style check.

## Tests

`OutWit.Controller.AI.Verify.Tests`: wire-layout guards, the schema guards,
the parser at its edges, the sandbox verdict, adversarial, hygiene, raw-WASI
and Python-layer suites, the worker pool (cancellation included) and the
benchmark, the taskset, sampling and integrity pipelines, the package format,
plan, transfer (a link that drops and comes back included) and the share's
progress, the end-to-end runs
of every bundled script through the engine on in-process nodes, and the
module self-containment proof. The sandbox suites need the pinned runtime set
(`verify-assets build OutWit.Controller.AI.Verify.Tests/@Data/runtimes`;
`@Data/` is gitignored) and Assert.Ignore without it; the `AI Verify` workflow
builds the set and runs everything on Linux, macOS and Windows.

Load tests (`Category=Load`, not in the sweep): a thousand tasks at the
machine's degree, thousands of runs with bounded memory, hundreds of distinct
raw modules against the bounded module cache, a hundred thousand lines
through the parser and the planner and a hundred thousand results through
every writer, and thousands of tasks through `VerifyRun` with sampling on
the in-process nodes — `dotnet test --filter Category=Load`.
