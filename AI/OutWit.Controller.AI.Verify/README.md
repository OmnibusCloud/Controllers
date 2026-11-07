# OutWit.Controller.AI.Verify

Sandboxed, deterministic, resource-metered execution of untrusted programs
against specifications — on anonymous volunteer nodes. One primitive
(`Verify.ExecuteBatch`) carries several products: code-vs-tests verification
(RLVR-style reward checking), code-corpus generation with compile/test
checking, math problem generation with CAS checking, and formal proof
checking — generation and verification are the same activity with different
programs.

**Status: 0.1.0 — the pipeline is complete and tested; the first fleet
deployment is in progress.** `Verify.Split` → `Grid.ForEach` ⇒
`Verify.ExecuteBatch` → `Verify.Collect` runs through the engine and returns
a byte-identical verdict report for a taskset, on Windows and Linux.

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
| `Verify.ExecuteBatch` | node | the primitive: one compiled runtime module, the batch's tasks executed concurrently in isolated stores under fuel, memory and wall-clock limits |
| `Verify.Execute` | node | the single-task variant |
| `Verify.Collect` | host | result batches → the verdict report (aggregates + one record per task; byte-deterministic) |
| `Verify.Preflight` | host | counts, runtimes referenced, unknown runtimes, size estimate, ceiling notes — before anything runs |
| `Verify.RuntimeDiagnostics` | node | wasmtime version, runtime target, per-runtime availability and hash |

Scripts (`OutWit.Controller.AI.Verify.Scripts`): `VerifyRun`, `VerifyPreflight`,
`VerifyRuntimeDiagnostics`.

## Runtimes

Language runtimes are WebAssembly modules, platform-independent and pinned by
SHA-256 in the controller's catalog: CPython 3.14.6 (WASI) and QuickJS-NG
0.15.1. A task names its runtime; a batch shares one compiled module.

Honest v1 scope: pure Python with the standard library, and JavaScript. No C
extensions inside WASI yet.

## What the module carries

This is the first controller that calls a native library in-process (wasmtime,
through the `Wasmtime.Dotnet` binding), so the deployed module is laid out for
the engine's plugin load context, which resolves native libraries from the
module's own `deps.json`:

```
verify.module/
  OutWit.Controller.AI.Verify.dll, .deps.json, controller.json
  OutWit.Controller.AI.Verify.Model.dll
  Wasmtime.Dotnet.dll                      staged by the build
  runtimes/<rid>/native/                   wasmtime, one data asset per RID
  wasm/                                    the language runtime modules, one data asset
```

The natives and the runtime modules are Tier-2 data assets (`ControllerDataAsset`),
fetched by consumers at build time from the `verify-v<version>` release of this
repository and verified by hash; the nupkg itself stays small. Licences travel
inside the asset archives (wasmtime: Apache-2.0; CPython: PSF; QuickJS-NG: MIT).

`VerifySelfCheck.Run()` reports, from inside a loaded module, where the binding
and the runtimes resolved from and whether one task per runtime ran — the
module-load test calls it through the real isolated loader, and an operator
can call it as a doctor-style check.

## Tests

`OutWit.Controller.AI.Verify.Tests`: wire-layout guards, the sandbox verdict
and adversarial suites, the taskset pipeline, the end-to-end run through the
engine on in-process nodes, and the module self-containment proof. The
sandbox suites are opt-in on the pinned runtime modules
(`download-spike-runtimes.ps1`, `@Data/` is gitignored).
