using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderPreflightStillTiled : WitActivityAdapterFunction<WitActivityRenderPreflightStillTiled>
{
    #region Constructors

    public WitActivityAdapterRenderPreflightStillTiled(
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
        var renderOptions = RenderBenchmarkHelper.CreateBenchmarkRenderOptions();
        var tileOptions = RenderBenchmarkHelper.CreateBenchmarkTileOptions();

        return await RenderBenchmarkHelper.MeasureAsync(
            options,
            unit: RenderBenchmarkHelper.PREFLIGHT_STILL_TILED_UNIT,
            datasetId: RenderBenchmarkHelper.PREFLIGHT_STILL_TILED_DATASET,
            action: async ct =>
            {
                var diagnostics = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ct);
                _ = RenderPreflightEvaluator.EvaluateStillTiled(
                    RenderBenchmarkHelper.BenchmarkTilesX,
                    RenderBenchmarkHelper.BenchmarkTilesY,
                    renderOptions,
                    tileOptions,
                    diagnostics);
            },
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Functions

    protected override WitActivityRenderPreflightStillTiled CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 4)
            throw new ArgumentException($"Render.PreflightStillTiled expects 4 parameters, got {parameters.Length}");

        return new WitActivityRenderPreflightStillTiled
        {
            TilesX = parameters[0],
            TilesY = parameters[1],
            Options = parameters[2],
            TileOptions = parameters[3]
        };
    }

    protected override async Task Process(
        WitActivityRenderPreflightStillTiled activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.TilesX, out int tilesX))
            throw new InvalidOperationException("Failed to get Int parameter 'tilesX'");

        if (!pool.TryGetValue(activity.TilesY, out int tilesY))
            throw new InvalidOperationException("Failed to get Int parameter 'tilesY'");

        if (!pool.TryGetValue(activity.Options, out RenderOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get RenderOptions parameter 'options'");

        if (!pool.TryGetValue(activity.TileOptions, out TileOptionsData? tileOptions) || tileOptions == null)
            throw new InvalidOperationException("Failed to get TileOptions parameter 'tileOptions'");

        var diagnostics = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ProcessingManager.CancellationToken(status.JobId));
        var result = RenderPreflightEvaluator.EvaluateStillTiled(tilesX, tilesY, options, tileOptions, diagnostics);

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.PreflightStillTiled.");
    }

    #endregion
}
