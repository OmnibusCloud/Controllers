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

internal sealed class WitActivityAdapterRenderPreflightFrames : WitActivityAdapterFunction<WitActivityRenderPreflightFrames>
{
    #region Constructors

    public WitActivityAdapterRenderPreflightFrames(
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

        return await RenderBenchmarkHelper.MeasureAsync(
            options,
            unit: RenderBenchmarkHelper.PREFLIGHT_FRAMES_UNIT,
            datasetId: RenderBenchmarkHelper.PREFLIGHT_FRAMES_DATASET,
            action: async ct =>
            {
                var diagnostics = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ct);
                _ = RenderPreflightEvaluator.EvaluateFrames(
                    RenderBenchmarkHelper.BenchmarkVideoStartFrame,
                    RenderBenchmarkHelper.BenchmarkVideoEndFrame,
                    renderOptions,
                    diagnostics);
            },
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Functions

    protected override WitActivityRenderPreflightFrames CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 3)
            throw new ArgumentException($"Render.PreflightFrames expects 3 parameters, got {parameters.Length}");

        return new WitActivityRenderPreflightFrames
        {
            StartFrame = parameters[0],
            EndFrame = parameters[1],
            Options = parameters[2]
        };
    }

    protected override async Task Process(
        WitActivityRenderPreflightFrames activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.StartFrame, out int startFrame))
            throw new InvalidOperationException("Failed to get Int parameter 'startFrame'");

        if (!pool.TryGetValue(activity.EndFrame, out int endFrame))
            throw new InvalidOperationException("Failed to get Int parameter 'endFrame'");

        if (!pool.TryGetValue(activity.Options, out RenderOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get RenderOptions parameter 'options'");

        var diagnostics = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ProcessingManager.CancellationToken(status.JobId));
        var result = RenderPreflightEvaluator.EvaluateFrames(startFrame, endFrame, options, diagnostics);

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.PreflightFrames.");
    }

    #endregion
}
