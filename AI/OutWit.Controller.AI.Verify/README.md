# OutWit.Controller.AI.Verify

Sandboxed, deterministic, resource-metered execution of untrusted programs
against specifications — on anonymous volunteer nodes. One primitive
(`Verify.ExecuteBatch`) carries several products: code-vs-tests verification
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
| `Verify.Split` | host | taskset (JSON lines) + options → batches, grouped by runtime, limits clamped to the host ceilings |
| `Verify.ExecuteBatch` | node | the primitive: one compiled runtime module, the batch's tasks executed concurrently in isolated stores under fuel, memory and wall-clock limits; per-task progress; batch provenance |
| `Verify.Execute` | node | the single-task variant |
| `Verify.Collect` | host | result batches → the verdict report (aggregates + one record per task; the run's identity, byte-identical regardless of placement) |
| `Verify.CollectOutputs` | host | result batches → the corpus (one JSON line per task with its outputs; byte-deterministic) |
| `Verify.MakeCheckTasks` | host | generator results + a checker spec → check task batches, one check per passing candidate under the candidate's index |
| `Verify.Provenance` | host | result batches → the provenance document (which node ran which tasks, with which wasmtime and runtime module, at which degree, and what each task cost: fuel, peak memory, wall time) |
| `Verify.Sample` | host | the primary batches + options → replica batches of the sampled share of tasks (`SamplingRate`), re-planned so the allocator places them on its own; empty at rate 0 |
| `Verify.Integrity` | host | primary + replica result batches → the integrity document (each sampled task paired with its re-execution, the nodes of each side, agreement on verdict, exit, outputs and cases; same-node pairs counted, not compared; the mismatch rate) |
| `Verify.Preflight` | host | counts, runtimes referenced, unknown runtimes, size estimate, ceiling notes — before anything runs |
| `Verify.RuntimeDiagnostics` | host | wasmtime version (the native library is loaded to prove it), runtime target, per-runtime availability and hash — of the server's module; a node's module is exercised by `Verify.ExecuteBatch` and described by the provenance |

## Scripts

`OutWit.Controller.AI.Verify.Scripts` bundles:

| Script | Shape | Returns |
|---|---|---|
| `VerifyRun(Blob taskset, VerifyOptions opts)` | Split → ForEach ⇒ ExecuteBatch → Sample → ForEach ⇒ ExecuteBatch → Collect, Provenance, Integrity | `report`, `provenance`, `integrity` |
| `VerifyGenerate(Blob taskset, VerifyOptions opts)` | the same fan-out; the programs are generators | `corpus`, `report`, `provenance`, `integrity` |
| `VerifyGenerateAndCheck(Blob generators, Blob checker, VerifyOptions opts)` | generators fan out → MakeCheckTasks → checks fan out (sampled checks re-run) → Collect | `report` (the checks), `corpus` (the candidates), `provenance`, `integrity` |
| `VerifyPreflight(Blob taskset, VerifyOptions opts)` | one host activity | `result` |
| `VerifyRuntimeDiagnostics()` | one host activity | `result` |

`VerifyOptions`: `DefaultLimits`, `BatchSize` (tasks per chunk, 0 = 32),
`MaxDegree` (concurrent sandboxes per node, 0 = 16), `SamplingRate` (the share
of tasks re-executed for integrity, 0–1; 0 = none), `ReportFormat` (reserved).

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

Anonymous nodes are trusted by checking, not by assumption. `SamplingRate`
re-executes a deterministic share of the tasks (a task is in the sample by
its index alone, so a lab can reproduce the choice) as a second fan-out the
allocator places on its own, and the integrity document pairs each sampled
task with its primary: the nodes of each side and whether the two agree on
verdict, exit code, outputs and per-case outcomes — exactly the fields an
honest execution reproduces anywhere. The replica batches reach the allocator
in reverse order, so a rate-partitioned placement puts most pairs on different
nodes; a pair that still landed on one node is counted, not compared (a node
cannot vouch for itself), and a distinct-node guarantee is the allocator's job
in a later version. The mismatch rate is over the compared pairs.

## Determinism

The verdict report is the run's identity: it carries verdicts, exit codes and
per-case outcomes — the fields two honest executions agree on wherever they
ran — and is byte-identical for a taskset regardless of which nodes ran which
batches (proven on the test fleet across three placements). Fuel is
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
MaxDegree)` — 16 by default. Per-task completion reaches the host's activity
progress sink about twice a second, so a long chunk moves the job's progress.
Every result batch carries its provenance: node, runtime target, wasmtime and
native library origin, runtime module hash, degree, task count, wall time.

The node benchmark (`verify-task@mixed-v1`) is the throughput of a fixed
reference batch — one CPU-bound Python program and one QuickJS program, four
per worker thread, with their expected outputs — as tasks per second of the
median run at the node's degree; a node that computes wrong cannot score, a
node without runtimes reports the default (unranked) score. `Grid.ForEach`
allocates batches with the engine's `Balanced` strategy (pre-partitioned by
rate), which the batch work estimate (task count) matches.

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
the sandbox verdict and adversarial suites, the worker pool and the benchmark,
the taskset and check-task pipelines, the end-to-end runs of every script
through the engine on in-process nodes, and the module self-containment
proof. The sandbox suites are opt-in on the pinned runtime set
(`verify-assets build OutWit.Controller.AI.Verify.Tests/@Data/runtimes`;
`@Data/` is gitignored).
