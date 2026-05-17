using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderRuntimeDiagnostics : WitActivityAdapterFunction<WitActivityRenderRuntimeDiagnostics>
{
    #region Constructors

    public WitActivityAdapterRenderRuntimeDiagnostics(
        IWitProcessingManager processingManager,
        ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Benchmarking

    public override async Task<IWitBenchmarkResult> RunBenchmark(
        IWitBenchmarkOptions? options,
        CancellationToken cancellationToken)
    {
        return await RenderBenchmarkHelper.MeasureAsync(
            options,
            unit: RenderBenchmarkHelper.RUNTIME_DIAGNOSTICS_UNIT,
            datasetId: RenderBenchmarkHelper.RUNTIME_DIAGNOSTICS_DATASET,
            action: async ct => _ = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ct),
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Functions

    protected override WitActivityRenderRuntimeDiagnostics CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 0)
            throw new ArgumentException($"Render.RuntimeDiagnostics expects 0 parameters, got {parameters.Length}");

        return new WitActivityRenderRuntimeDiagnostics();
    }

    protected override async Task Process(
        WitActivityRenderRuntimeDiagnostics activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        var diagnostics = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ProcessingManager.CancellationToken(status.JobId));

        Logger.LogInformation(
            "Render.RuntimeDiagnostics completed for job {JobId}: SelectedRenderBackend={SelectedRenderBackend}, UsesGpuForRendering={UsesGpuForRendering}, AvailableRenderBackends={AvailableRenderBackends}, Message={Message}",
            status.JobId,
            diagnostics.SelectedRenderBackend ?? "none",
            diagnostics.UsesGpuForRendering,
            diagnostics.AvailableRenderBackends.Length == 0 ? "none" : string.Join(",", diagnostics.AvailableRenderBackends),
            diagnostics.RenderBackendSelectionMessage ?? "none");

        if (!pool.TrySetValue(activity.ReturnReference, diagnostics))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.RuntimeDiagnostics.");
    }

    #endregion
}
