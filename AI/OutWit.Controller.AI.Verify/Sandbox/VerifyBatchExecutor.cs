using System.Diagnostics;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Runs one task batch on a node: resolves the batch's runtime, picks the degree (cores, the
/// memory that is free, the batch's cap), executes the tasks on the below-normal worker pool,
/// and returns one result per task plus the batch's provenance.
/// <para>
/// What the node can know before it starts is checked before it starts
/// (<see cref="EnsureRunnable"/>): a runtime it does not have, or has other bytes of, refuses the
/// batch whole, so the work goes to another node and nothing is lost or reported as a verdict.
/// What goes wrong for one task while the batch runs is that task's result without a verdict
/// (<see cref="VerifyVerdict.NoResult"/>). The limits a task runs under are clamped to the
/// ceilings here, whatever the batch says: a node holds itself to them.
/// </para>
/// </summary>
public static class VerifyBatchExecutor
{
    #region Constants

    /// <summary>The most tasks a batch may hold: a node takes no larger one.</summary>
    public const int MAX_BATCH_TASKS = 4096;

    #endregion

    #region Functions

    /// <summary>
    /// Executes a batch without progress reporting (tests and local runs).
    /// </summary>
    /// <param name="sandbox">The node's sandbox.</param>
    /// <param name="runtimesRoot">The language runtime root, or null when none was found.</param>
    /// <param name="batch">The batch.</param>
    /// <returns>One result per task plus the batch diagnostics.</returns>
    /// <exception cref="VerifyBatchRefusedException">This node cannot run the batch: its runtime is missing or is not the pinned one, or the batch is larger than a node takes.</exception>
    public static VerifyResultBatchData Execute(VerifyWasmSandbox sandbox, string? runtimesRoot, VerifyTaskBatchData batch)
    {
        return Execute(sandbox, runtimesRoot, batch, null);
    }

    /// <summary>
    /// Executes a batch on this node.
    /// </summary>
    /// <param name="sandbox">The node's sandbox.</param>
    /// <param name="runtimesRoot">The language runtime root, or null when none was found.</param>
    /// <param name="batch">The batch.</param>
    /// <param name="progress">Called after each task with (completed, total), from the worker that finished it.</param>
    /// <returns>One result per task plus the batch diagnostics.</returns>
    /// <exception cref="VerifyBatchRefusedException">This node cannot run the batch: its runtime is missing or is not the pinned one, or the batch is larger than a node takes.</exception>
    public static VerifyResultBatchData Execute(VerifyWasmSandbox sandbox, string? runtimesRoot, VerifyTaskBatchData batch, Action<int, int>? progress)
    {
        return Execute(sandbox, runtimesRoot, batch, progress, CancellationToken.None);
    }

    /// <summary>
    /// Executes a batch on this node, stopping at the next task boundary when the job is cancelled.
    /// </summary>
    /// <param name="sandbox">The node's sandbox.</param>
    /// <param name="runtimesRoot">The language runtime root, or null when none was found.</param>
    /// <param name="batch">The batch.</param>
    /// <param name="progress">Called after each task with (completed, total), from the worker that finished it.</param>
    /// <param name="cancellationToken">The job's token: no further task starts once cancelled, a running one ends within its budget.</param>
    /// <param name="onNodeFault">Told the task and what failed on the node when a task has no verdict for that; for the node's own log.</param>
    /// <returns>One result per task plus the batch diagnostics.</returns>
    /// <exception cref="OperationCanceledException">The job was cancelled before the batch finished.</exception>
    /// <exception cref="VerifyBatchRefusedException">This node cannot run the batch: its runtime is missing or is not the pinned one, or the batch is larger than a node takes.</exception>
    public static VerifyResultBatchData Execute(VerifyWasmSandbox sandbox, string? runtimesRoot, VerifyTaskBatchData batch, Action<int, int>? progress,
        CancellationToken cancellationToken, Action<int, Exception>? onNodeFault = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var runtime = EnsureRunnable(runtimesRoot, batch);
        var results = new VerifyResultData[batch.Tasks.Count];

        var degree = VerifySandboxDegree.ForCurrentMachine(MemoryPerTask(batch, runtime), batch.MaxDegree);
        var completed = 0;

        VerifyWorkerPool.Run(batch.Tasks.Count, degree,
            i =>
            {
                var task = batch.Tasks[i];
                var limits = VerifyLimitCeilings.Effective(task.Limits, batch.DefaultLimits);
                results[i] = sandbox.Execute(runtime, task, limits, cancellationToken, fault => onNodeFault?.Invoke(task.TaskIndex, fault));
            },
            _ => progress?.Invoke(Interlocked.Increment(ref completed), batch.Tasks.Count),
            cancellationToken);

        stopwatch.Stop();
        var wallMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);

        return new VerifyResultBatchData
        {
            Results = results.ToList(),
            Diagnostics = VerifyBatchDiagnosticsBuilder.Build(runtime.Descriptor.Id, runtime, degree, results.Length, wallMs)
        };
    }

    /// <summary>
    /// Checks what this node can know about a batch before running any of it: the runtime is one
    /// it has, with the bytes the server's controller pins, and the batch is no larger than a node
    /// takes.
    /// </summary>
    /// <param name="runtimesRoot">The language runtime root, or null when none was found.</param>
    /// <param name="batch">The batch.</param>
    /// <returns>The batch's runtime, resolved.</returns>
    /// <exception cref="VerifyBatchRefusedException">This node cannot run the batch; the message is fixed words without a path.</exception>
    public static VerifyResolvedRuntime EnsureRunnable(string? runtimesRoot, VerifyTaskBatchData batch)
    {
        if (batch.Tasks.Count > MAX_BATCH_TASKS)
            throw new VerifyBatchRefusedException($"a batch holds {batch.Tasks.Count} tasks; a node takes no more than {MAX_BATCH_TASKS}", null);

        // What a taskset may hold is what a node takes, whoever made the batch: a task runs once
        // a case and every source is a file, so their numbers are bounded here as well.
        foreach (var task in batch.Tasks)
        {
            if (!IsWithinTaskBounds(task))
                throw new VerifyBatchRefusedException("a task of the batch has more sources, arguments or cases than a node takes", null);
        }

        var runtime = VerifyRuntimeCatalog.Resolve(runtimesRoot, batch.RuntimeId, out var reason, out var detail);
        if (runtime == null)
            throw new VerifyBatchRefusedException($"runtime '{Shown(batch.RuntimeId)}' cannot be used on this node: {reason}", detail);

        // One controller version a job means one set of runtime bytes; a batch that names other
        // bytes was made by a controller this node is not.
        if (batch.RuntimeSha256.Length > 0 && !string.Equals(batch.RuntimeSha256, runtime.Descriptor.Sha256, StringComparison.Ordinal))
            throw new VerifyBatchRefusedException($"runtime '{Shown(batch.RuntimeId)}' is pinned to other bytes by the controller that made the batch", null);

        return runtime;
    }

    /// <summary>
    /// Whether a task has no more sources, arguments and cases than a taskset may give one
    /// (<see cref="VerifyTasksetLimits"/>).
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>False for a task no honest server sends.</returns>
    public static bool IsWithinTaskBounds(VerifyTaskData task)
    {
        if (task.Sources == null || task.Args == null)
            return false;

        if (task.Sources.Count > VerifyTasksetLimits.MAX_SOURCES || task.Args.Count > VerifyTasksetLimits.MAX_ARGS)
            return false;

        var cases = task.Suite?.Cases;
        if (cases == null)
            return task.Suite == null;

        return cases.Count <= VerifyTasksetLimits.MAX_SUITE_CASES
               && cases.All(item => item?.Args != null && item.Args.Count <= VerifyTasksetLimits.MAX_ARGS);
    }

    /// <summary>
    /// The most memory one task of the batch may take while it runs: its own cap, or the cap of
    /// the compiler's steps when the batch is of the C/C++ runtime and that is the larger.
    /// </summary>
    internal static long MemoryPerTask(VerifyTaskBatchData batch, VerifyResolvedRuntime runtime)
    {
        long cap = 0;
        foreach (var task in batch.Tasks)
            cap = Math.Max(cap, VerifyLimitCeilings.Effective(task.Limits, batch.DefaultLimits).MemoryBytes);

        if (cap <= 0)
            cap = VerifySandboxDefaults.DEFAULT_MEMORY_BYTES;

        return runtime.Descriptor.Language == VerifyRuntimeLanguage.C ? Math.Max(cap, VerifyClangBuild.STEP_MEMORY_BYTES) : cap;
    }

    /// <summary>A runtime id as a message may show it: bounded, and only the characters an id has.</summary>
    private static string Shown(string runtimeId)
    {
        var shown = runtimeId.Length > 64 ? runtimeId[..64] : runtimeId;
        return string.Concat(shown.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '?'));
    }

    #endregion
}
