# OutWit.Controller.AI.Verify.Model

Data types for the OutWit Verify controller: tasks, batches, test suites,
resource limits, and results for sandboxed execution.

Tasks (`VerifyTaskData` with sources, entry point, args, stdin, seed, an
optional `VerifySuiteData` of cases), batches (`VerifyTaskBatchData` with
default `VerifyLimitsData`), results (`VerifyResultData`, `VerifyResultBatchData`
with `VerifyBatchDiagnosticsData`), options, preflight and runtime diagnostics.
Every type derives from `ModelBase` and is MemoryPack-serializable with
explicit, append-only member order.
