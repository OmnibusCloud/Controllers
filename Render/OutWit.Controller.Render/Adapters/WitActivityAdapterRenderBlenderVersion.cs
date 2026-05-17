using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderBlenderVersion : WitActivityAdapterFunction<WitActivityRenderBlenderVersion>
{
    #region Constructors

    public WitActivityAdapterRenderBlenderVersion(
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
        var runner = RenderBenchmarkHelper.TryCreateBlenderRunner(Logger);
        if (runner == null)
        {
            Logger.LogWarning("Blender version benchmark skipped because Blender is unavailable.");
            return RenderBenchmarkHelper.CreateUnavailableResult(
                RenderBenchmarkHelper.BLENDER_VERSION_UNIT,
                RenderBenchmarkHelper.RUNTIME_DIAGNOSTICS_DATASET);
        }

        return await RenderBenchmarkHelper.MeasureAsync(
            options,
            unit: RenderBenchmarkHelper.BLENDER_VERSION_UNIT,
            datasetId: RenderBenchmarkHelper.RUNTIME_DIAGNOSTICS_DATASET,
            action: async ct => _ = await runner.GetVersionAsync(ct),
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Functions

    protected override WitActivityRenderBlenderVersion CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 0)
            throw new ArgumentException($"Render.BlenderVersion expects 0 parameters, got {parameters.Length}");

        return new WitActivityRenderBlenderVersion();
    }

    protected override async Task Process(
        WitActivityRenderBlenderVersion activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        var version = await GetBlenderRunner().GetVersionAsync(ProcessingManager.CancellationToken(status.JobId));
        if (!pool.TrySetValue(activity.ReturnReference, version))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.BlenderVersion.");
    }

    private BlenderRunner GetBlenderRunner()
    {
        var runner = RenderBenchmarkHelper.TryCreateBlenderRunner(Logger);
        if (runner == null)
            throw new InvalidOperationException("Blender is not available in the current render controller runtime.");

        return runner;
    }

    #endregion
}
