using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Runs a task batch on a node: resolves+verifies the batch's runtime once, then executes
/// the tasks concurrently at the memory-bounded degree (a single WASM task is
/// single-threaded, so one-at-a-time would waste every core but one). A poison task
/// yields its own verdict and never fails the batch. Isolates the node-side execution
/// policy from the engine's activity plumbing so it can be unit-tested directly.
/// </summary>
public static class VerifyBatchExecutor
{
    public static VerifyResultBatchData Execute(VerifyWasmSandbox sandbox, string? runtimesRoot, VerifyTaskBatchData batch)
    {
        var results = new VerifyResultData[batch.Tasks.Count];

        if (runtimesRoot == null)
        {
            FillUnavailable(results, batch, $"runtimes root not found (set {VerifyRuntimeLocator.OverrideEnvVar} or stage the runtime archive)");
            return new VerifyResultBatchData { Results = results.ToList() };
        }

        var runtime = VerifyRuntimeCatalog.Resolve(runtimesRoot, batch.RuntimeId, out var reason);
        if (runtime == null)
        {
            FillUnavailable(results, batch, reason ?? $"runtime '{batch.RuntimeId}' unavailable");
            return new VerifyResultBatchData { Results = results.ToList() };
        }

        var perTaskMemoryCap = MaxMemoryCap(batch);
        var degree = VerifySandboxDegree.ForCurrentMachine(perTaskMemoryCap);

        Parallel.For(0, batch.Tasks.Count, new ParallelOptions { MaxDegreeOfParallelism = degree }, i =>
        {
            var task = batch.Tasks[i];
            var limits = VerifySandboxDefaults.Resolve(task.Limits, batch.DefaultLimits);
            results[i] = sandbox.Execute(runtime, task, limits);
        });

        return new VerifyResultBatchData { Results = results.ToList() };
    }

    private static long MaxMemoryCap(VerifyTaskBatchData batch)
    {
        long cap = 0;
        foreach (var task in batch.Tasks)
        {
            var limits = VerifySandboxDefaults.Resolve(task.Limits, batch.DefaultLimits);
            cap = Math.Max(cap, limits.MemoryBytes);
        }

        return cap > 0 ? cap : VerifySandboxDefaults.DEFAULT_MEMORY_BYTES;
    }

    private static void FillUnavailable(VerifyResultData[] results, VerifyTaskBatchData batch, string reason)
    {
        for (var i = 0; i < results.Length; i++)
        {
            results[i] = new VerifyResultData
            {
                TaskIndex = batch.Tasks[i].TaskIndex,
                Verdict = VerifyVerdict.RuntimeUnavailable,
                ExitCode = -1,
                Stderr = reason
            };
        }
    }
}
