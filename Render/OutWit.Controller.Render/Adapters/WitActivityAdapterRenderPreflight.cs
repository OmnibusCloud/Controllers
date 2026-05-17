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

internal sealed class WitActivityAdapterRenderPreflight : WitActivityAdapterFunction<WitActivityRenderPreflight>
{
    #region Constructors

    public WitActivityAdapterRenderPreflight(
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
        if (RenderBenchmarkHelper.FindStillBenchmarkScene() == null || RenderBenchmarkHelper.FindVideoBenchmarkScene() == null)
        {
            Logger.LogWarning("Render.Preflight benchmark skipped because one or more canonical benchmark scenes are missing.");
            return RenderBenchmarkHelper.CreateUnavailableResult(
                RenderBenchmarkHelper.PREFLIGHT_UNIT,
                RenderBenchmarkHelper.PREFLIGHT_DATASET);
        }

        var renderOptions = RenderBenchmarkHelper.CreateBenchmarkRenderOptions();
        var tileOptions = RenderBenchmarkHelper.CreateBenchmarkTileOptions();
        var videoOptions = RenderBenchmarkHelper.CreateBenchmarkVideoOptions();

        return await RenderBenchmarkHelper.MeasureAsync(
            options,
            unit: RenderBenchmarkHelper.PREFLIGHT_UNIT,
            datasetId: RenderBenchmarkHelper.PREFLIGHT_DATASET,
            action: async ct =>
            {
                var diagnostics = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ct);
                var still = RenderPreflightEvaluator.EvaluateFrames(
                    RenderBenchmarkHelper.BenchmarkStillFrame,
                    RenderBenchmarkHelper.BenchmarkStillFrame,
                    renderOptions,
                    diagnostics);
                var frames = RenderPreflightEvaluator.EvaluateFrames(
                    RenderBenchmarkHelper.BenchmarkVideoStartFrame,
                    RenderBenchmarkHelper.BenchmarkVideoEndFrame,
                    renderOptions,
                    diagnostics);
                var stillTiled = RenderPreflightEvaluator.EvaluateStillTiled(
                    RenderBenchmarkHelper.BenchmarkTilesX,
                    RenderBenchmarkHelper.BenchmarkTilesY,
                    renderOptions,
                    tileOptions,
                    diagnostics);
                var video = RenderPreflightEvaluator.EvaluateVideo(renderOptions, videoOptions, diagnostics);

                _ = CreatePreflightResult(diagnostics, still, frames, stillTiled, video);
            },
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Functions

    protected override WitActivityRenderPreflight CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 8)
            throw new ArgumentException($"Render.Preflight expects 8 parameters, got {parameters.Length}");

        return new WitActivityRenderPreflight
        {
            Frame = parameters[0],
            StartFrame = parameters[1],
            EndFrame = parameters[2],
            TilesX = parameters[3],
            TilesY = parameters[4],
            Options = parameters[5],
            TileOptions = parameters[6],
            Video = parameters[7]
        };
    }

    protected override async Task Process(
        WitActivityRenderPreflight activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        var frame = GetRequiredValue<int>(pool, activity.Frame, "Int", "frame");
        var startFrame = GetRequiredValue<int>(pool, activity.StartFrame, "Int", "startFrame");
        var endFrame = GetRequiredValue<int>(pool, activity.EndFrame, "Int", "endFrame");
        var tilesX = GetRequiredValue<int>(pool, activity.TilesX, "Int", "tilesX");
        var tilesY = GetRequiredValue<int>(pool, activity.TilesY, "Int", "tilesY");
        var options = GetRequiredValue<RenderOptionsData>(pool, activity.Options, "RenderOptions", "options");
        var tileOptions = GetRequiredValue<TileOptionsData>(pool, activity.TileOptions, "TileOptions", "tileOptions");
        var video = GetRequiredValue<VideoOptionsData>(pool, activity.Video, "VideoOptions", "video");

        var diagnostics = await RenderRuntimeDiagnosticsBuilder.BuildAsync(Logger, ProcessingManager.CancellationToken(status.JobId));
        var still = RenderPreflightEvaluator.EvaluateFrames(frame, frame, options, diagnostics);
        var frames = RenderPreflightEvaluator.EvaluateFrames(startFrame, endFrame, options, diagnostics);
        var stillTiled = RenderPreflightEvaluator.EvaluateStillTiled(tilesX, tilesY, options, tileOptions, diagnostics);
        var videoResult = RenderPreflightEvaluator.EvaluateVideo(options, video, diagnostics);

        var result = CreatePreflightResult(diagnostics, still, frames, stillTiled, videoResult);

        if (result.CanRenderAll)
        {
            Logger.LogDebug(
                "Render.Preflight completed for job {JobId}: SelectedRenderBackend={SelectedRenderBackend}, UsesGpuForRendering={UsesGpuForRendering}",
                status.JobId,
                diagnostics.SelectedRenderBackend ?? "none",
                diagnostics.UsesGpuForRendering);
        }
        else
        {
            Logger.LogWarning(
                "Render.Preflight reported blocking issues for job {JobId}: SelectedRenderBackend={SelectedRenderBackend}, UsesGpuForRendering={UsesGpuForRendering}, BlockingIssueCount={BlockingIssueCount}, Message={Message}",
                status.JobId,
                diagnostics.SelectedRenderBackend ?? "none",
                diagnostics.UsesGpuForRendering,
                CountIssues(result),
                diagnostics.RenderBackendSelectionMessage ?? "none");
        }

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.Preflight.");
    }

    private static int CountIssues(RenderPreflightData result)
    {
        return (result.Still?.Issues.Count ?? 0)
               + (result.Frames?.Issues.Count ?? 0)
               + (result.StillTiled?.Issues.Count ?? 0)
               + (result.Video?.Issues.Count ?? 0);
    }

    private static RenderPreflightData CreatePreflightResult(
        RenderRuntimeDiagnosticsData diagnostics,
        RenderPreflightFramesData still,
        RenderPreflightFramesData frames,
        RenderPreflightStillTiledData stillTiled,
        RenderPreflightVideoData video)
    {
        return new RenderPreflightData
        {
            RuntimeDiagnostics = diagnostics,
            Still = still,
            Frames = frames,
            StillTiled = stillTiled,
            Video = video,
            CanRenderAll = still.CanRender && frames.CanRender && stillTiled.CanRender && video.CanRender
        };
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
