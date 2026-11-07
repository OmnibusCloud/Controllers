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
| `Verify.Preflight` | host | counts, runtimes referenced, unknown runtimes, size estimate, ceiling notes — before anything runs |
| `Verify.RuntimeDiagnostics` | host | wasmtime version (the native library is loaded to prove it), runtime target, per-runtime availability and hash — of the server's module; a node's module is exercised by `Verify.ExecuteBatch` and described by the provenance |

## Scripts

`OutWit.Controller.AI.Verify.Scripts` bundles:

| Script | Shape | Returns |
|---|---|---|
| `VerifyRun(Blob taskset, VerifyOptions opts)` | Split → ForEach ⇒ ExecuteBatch → Collect, Provenance | `report`, `provenance` |
| `VerifyGenerate(Blob taskset, VerifyOptions opts)` | the same fan-out; the programs are generators | `corpus`, `report`, `provenance` |
| `VerifyGenerateAndCheck(Blob generators, Blob checker, VerifyOptions opts)` | generators fan out → MakeCheckTasks → checks fan out → Collect | `report` (the checks), `corpus` (the candidates), `provenance` |
| `VerifyPreflight(Blob taskset, VerifyOptions opts)` | one host activity | `result` |
| `VerifyRuntimeDiagnostics()` | one host activity | `result` |

`VerifyOptions`: `DefaultLimits`, `BatchSize` (tasks per chunk, 0 = 32),
`MaxDegree` (concurrent sandboxes per node, 0 = 16), `SamplingRate` (reserved),
`ReportFormat` (reserved).

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

## Determinism

The verdict report is the run's identity: it carries verdicts, exit codes,
per-case outcomes and peak memory — the fields two honest executions agree on
wherever they ran — and is byte-identical for a taskset regardless of which
nodes ran which batches. Fuel is reproducible per host but drifts by about
0.01–0.06 % between hosts for CPython tasks (the import machinery scans the
stdlib directory and each file system lists it its own way), while QuickJS
tasks are byte-equal everywhere; wall time is hardware. Both are metrics and
live per task in the provenance document. The sandbox pins the clock and seeds
the random source from the task, so a program sees the same world on every
node.

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
SHA-256 in the controller's catalog: CPython 3.14.6 (WASI) and QuickJS-NG
0.15.1. A task names its runtime; a batch shares one compiled module.

Honest v1 scope: pure Python with the standard library, and JavaScript. No C
extensions inside WASI yet.

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
proof. The sandbox suites are opt-in on the pinned runtime modules
(`download-spike-runtimes.ps1`, `@Data/` is gitignored).
