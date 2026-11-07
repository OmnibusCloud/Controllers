using System.Diagnostics;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Runs one task batch on a node: resolves the batch's runtime, picks the degree (cores, memory,
/// the batch's cap), executes the tasks on the below-normal worker pool, and returns one verdict
/// per task plus the batch's provenance. A missing runtime is a per-task RuntimeUnavailable
/// verdict, never an exception — the chunk always comes back.
/// </summary>
public static class VerifyBatchExecutor
{
    #region Functions

    /// <summary>
    /// Executes a batch without node attribution or progress (tests and local runs).
    /// </summary>
    /// <param name="sandbox">The node's sandbox.</param>
    /// <param name="runtimesRoot">The language runtime root, or null when none was found.</param>
    /// <param name="batch">The batch.</param>
    /// <returns>One result per task plus the batch diagnostics.</returns>
    public static VerifyResultBatchData Execute(VerifyWasmSandbox sandbox, string? runtimesRoot, VerifyTaskBatchData batch)
    {
        return Execute(sandbox, runtimesRoot, batch, Guid.Empty, null);
    }

    /// <summary>
    /// Executes a batch on this node.
    /// </summary>
    /// <param name="sandbox">The node's sandbox.</param>
    /// <param name="runtimesRoot">The language runtime root, or null when none was found.</param>
    /// <param name="batch">The batch.</param>
    /// <param name="nodeId">This node's engine id, recorded in the diagnostics.</param>
    /// <param name="progress">Called after each task with (completed, total), from the worker that finished it.</param>
    /// <returns>One result per task plus the batch diagnostics.</returns>
    public static VerifyResultBatchData Execute(VerifyWasmSandbox sandbox, string? runtimesRoot, VerifyTaskBatchData batch, Guid nodeId, Action<int, int>? progress)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new VerifyResultData[batch.Tasks.Count];

        // A missing root is one more unavailable reason (the raw WASI runtime needs none).
        var runtime = VerifyRuntimeCatalog.Resolve(runtimesRoot, batch.RuntimeId, out var reason);
        if (runtime == null)
        {
            FillUnavailable(results, batch, reason ?? $"runtime '{batch.RuntimeId}' unavailable");
            return Finish(results, batch, nodeId, null, 0, stopwatch);
        }

        var perTaskMemoryCap = MaxMemoryCap(batch);
        var degree = VerifySandboxDegree.ForCurrentMachine(perTaskMemoryCap, batch.MaxDegree);
        var completed = 0;

        VerifyWorkerPool.Run(batch.Tasks.Count, degree,
            i =>
            {
                var task = batch.Tasks[i];
                var limits = VerifySandboxDefaults.Resolve(task.Limits, batch.DefaultLimits);
                results[i] = sandbox.Execute(runtime, task, limits);
            },
            _ => progress?.Invoke(Interlocked.Increment(ref completed), batch.Tasks.Count));

        return Finish(results, batch, nodeId, runtime, degree, stopwatch);
    }

    private static VerifyResultBatchData Finish(VerifyResultData[] results, VerifyTaskBatchData batch, Guid nodeId, VerifyResolvedRuntime? runtime, int degree, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        var wallMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);

        return new VerifyResultBatchData
        {
            Results = results.ToList(),
            Diagnostics = VerifyBatchDiagnosticsBuilder.Build(nodeId, batch.RuntimeId, runtime, degree, results.Length, wallMs)
        };
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

    #endregion
}
