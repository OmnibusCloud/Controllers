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

internal sealed class WitActivityAdapterRenderPreflightVideo : WitActivityAdapterFunction<WitActivityRenderPreflightVideo>
{
    #region Constructors

    public WitActivityAdapterRenderPreflightVideo(
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
        if (RenderBenchmarkHelper.FindVideoBenchmarkScene() == null)
        {
            Logger.LogWarning("Render.PreflightVideo benchmark skipped because the canonical video benchmark scene is missing.");
            return RenderBenchmarkHelper.CreateUnavailableResult(
                RenderBenchmarkHelper.PREFLIGHT_VIDEO_UNIT,
                RenderBenchmarkHelper.VIDEO_BENCHMARK_SCENE_DATASET);
        }

        var renderOptions = RenderBenchmarkHelper.CreateBenchmarkRenderOptions();
        var videoOptions = RenderBenchmarkHelper.CreateBenchmarkVideoOptions();

        return await RenderBenchmarkHelper.MeasureAsync(
            options,
            unit: RenderBenchmarkHelper.PREFLIGHT_VIDEO_UNIT,
            datasetId: RenderBenchmarkHelper.PREFLIGHT_VIDEO_DATASET,
            action: async ct =>
            {
                var diagnostics = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ct);
                _ = RenderPreflightEvaluator.EvaluateVideo(renderOptions, videoOptions, diagnostics);
            },
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Functions

    protected override WitActivityRenderPreflightVideo CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException($"Render.PreflightVideo expects 2 parameters, got {parameters.Length}");

        return new WitActivityRenderPreflightVideo
        {
            Options = parameters[0],
            Video = parameters[1]
        };
    }

    protected override async Task Process(
        WitActivityRenderPreflightVideo activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        var options = GetRequiredValue<RenderOptionsData>(pool, activity.Options, "RenderOptions", "options");
        var video = GetRequiredValue<VideoOptionsData>(pool, activity.Video, "VideoOptions", "video");

        var diagnostics = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ProcessingManager.CancellationToken(status.JobId));
        var result = RenderPreflightEvaluator.EvaluateVideo(options, video, diagnostics);

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.PreflightVideo.");
    }

    private static T GetRequiredValue<T>(IWitVariablesCollection pool, IWitParameter? reference, string typeName, string parameterName)
    {
        if (reference == null)
            throw new InvalidOperationException($"Missing parameter reference for {typeName} '{parameterName}'");

        if (!pool.TryGetValue(reference, out T? value) || value == null)
            throw new InvalidOperationException($"Failed to get {typeName} parameter '{parameterName}'");

        return value;
    }

    #endregion
}
