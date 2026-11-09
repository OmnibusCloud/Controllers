using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Assembles the per-batch provenance a node attaches to its result batch: with which wasmtime
/// and runtime module it ran, at which degree, and how long it took. It says nothing about
/// which machine ran it.
/// </summary>
public static class VerifyBatchDiagnosticsBuilder
{
    #region Functions

    /// <summary>
    /// Builds the diagnostics for one executed (or refused) batch.
    /// </summary>
    /// <param name="runtimeId">The batch's runtime id.</param>
    /// <param name="runtime">The resolved runtime, or null when it was unavailable on this node.</param>
    /// <param name="degree">The degree the batch ran at (0 when it did not run).</param>
    /// <param name="taskCount">Tasks in the batch.</param>
    /// <param name="wallMs">Wall time of the batch.</param>
    /// <returns>The diagnostics record.</returns>
    public static VerifyBatchDiagnosticsData Build(string runtimeId, VerifyResolvedRuntime? runtime, int degree, int taskCount, int wallMs)
    {
        return new VerifyBatchDiagnosticsData
        {
            RuntimeTarget = VerifyRuntimeDiagnosticsBuilder.RuntimeTarget(),
            WasmtimeVersion = typeof(Wasmtime.Engine).Assembly.GetName().Version?.ToString() ?? "unknown",
            RuntimeId = runtimeId,
            RuntimeSha256 = runtime?.Descriptor.Sha256 ?? "",
            Degree = degree,
            TaskCount = taskCount,
            WallMs = wallMs,
            ControllerVersion = ControllerBuildInfo.VERSION
        };
    }

    #endregion
}
