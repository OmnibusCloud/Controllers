using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

/// <summary>
/// Persistent-batch counterpart of <see cref="WitActivityAdapterRenderFrameBase{TActivity}"/>: renders
/// a whole chunk (<see cref="RenderTaskBatchData"/>) in a SINGLE Blender process (the .blend is loaded
/// once, every task rendered in-process) and returns a <see cref="RenderResultBatchData"/>. Reuses
/// <see cref="RenderFrameOutputHelper"/> for tile-normalization / result-building so behaviour matches
/// the per-frame path exactly. The benchmark is unchanged (render-only throughput) — once execution is
/// batched it already matches reality, so it weights Grid correctly for all engines.
/// </summary>
internal abstract class WitActivityAdapterRenderFrameBatchBase<TActivity> : WitActivityAdapterFunction<TActivity>
    where TActivity : WitActivityFunction, IRenderFrameActivity, new()
{
    #region Constants

    private const int DEFAULT_RESOLUTION_X = 1920;
    private const int DEFAULT_RESOLUTION_Y = 1080;
    private const int DEFAULT_SAMPLES = 128;

    #endregion

    #region Constructors

    protected WitActivityAdapterRenderFrameBatchBase(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        IWitTempStorage tempStorage,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        TempStorage = tempStorage;
    }

    #endregion

    #region Functions

    protected abstract RenderEngine BenchmarkEngine { get; }

    protected virtual string FrameBenchmarkDatasetId => RenderBenchmarkHelper.GetFrameBenchmarkDatasetId(BenchmarkEngine);

    protected virtual bool RequiresMatchingTaskEngine => true;

    protected override TActivity CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException($"{GetActivityName()} expects 1 parameter (RenderTaskBatch), got {parameters.Length}");

        return new TActivity
        {
            Task = parameters[0]
        };
    }

    protected override async Task Process(
        TActivity activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Task, out RenderTaskBatchData? batch) || batch == null)
            throw new InvalidOperationException($"Failed to get RenderTaskBatch parameter for {GetActivityName()}.");

        if (batch.Tasks.Count == 0)
            throw new InvalidOperationException($"{GetActivityName()} received an empty batch.");

        ValidateBatchEngine(batch);
        ProcessingManager.ThrowIfCancellationRequested(status.JobId);

        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var blendPath = await BlobService.GetLocalPathAsync(batch.SceneBlobId);
        var outputDir = RenderFrameOutputHelper.CreateRenderOutputDirectory(TempStorage, status.JobId, batch.Tasks[0].TaskIndex);

        try
        {
            var runner = GetBlenderRunner();
            var rendered = await runner.RenderFrameBatchAsync(blendPath, batch.Tasks, outputDir, batch.Options, cancellationToken);

            var results = new List<RenderResultData>(rendered.Count);
            foreach (var item in rendered)
            {
                var normalized = await RenderFrameOutputHelper.NormalizeRenderedTileOutputAsync(
                    GetFfmpegRunner(), Logger, GetActivityName(), item.RenderedPath, item.Task, outputDir, cancellationToken);

                var imageBlobId = await BlobService.UploadFileAsync(normalized.RenderedPath);
                results.Add(RenderFrameOutputHelper.CreateRenderResult(item.Task, imageBlobId, normalized.UseLogicalTileBounds));
            }

            var resultBatch = new RenderResultBatchData { Results = results };
            if (!pool.TrySetValue(activity.ReturnReference, resultBatch))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for {GetActivityName()}.");
        }
        finally
        {
            RenderFrameOutputHelper.CleanupRenderOutput(outputDir, renderedPath: null);
        }
    }

    #endregion

    #region Benchmarking

    protected override double EstimateWork(TActivity activity, IWitVariablesCollection pool)
    {
        if (!pool.TryGetValue(activity.Task, out RenderTaskBatchData? batch) || batch == null)
            return 1.0;

        double total = 0;
        foreach (var task in batch.Tasks)
        {
            var options = task.Options;
            long resX = options.ResolutionX > 0 ? options.ResolutionX : DEFAULT_RESOLUTION_X;
            long resY = options.ResolutionY > 0 ? options.ResolutionY : DEFAULT_RESOLUTION_Y;
            long samples = options.Samples > 0 ? options.Samples : DEFAULT_SAMPLES;
            double tileFraction = (task.TileMaxX - task.TileMinX) * (task.TileMaxY - task.TileMinY);
            total += resX * resY * samples * tileFraction;
        }

        return total > 0 ? total : 1.0;
    }

    public override async Task<IWitBenchmarkResult> RunBenchmark(
        IWitBenchmarkOptions? options,
        CancellationToken cancellationToken)
    {
        var runner = GetBlenderRunner();
        if (!runner.IsAvailable)
            return RenderBenchmarkHelper.CreateUnavailableResult(RenderBenchmarkHelper.FRAME_UNIT, FrameBenchmarkDatasetId);

        // Same render-only benchmark as Render.Frame — and now FAITHFUL, because FrameBatch executes
        // exactly as the benchmark does (many frames in one process).
        var result = await RenderBenchmarkHelper.MeasureRenderAsync(
            runner,
            BenchmarkEngine,
            options,
            unit: RenderBenchmarkHelper.FRAME_UNIT,
            datasetId: FrameBenchmarkDatasetId,
            cancellationToken: cancellationToken);

        Logger.LogInformation(
            "{ActivityName} benchmark: {Rate:F0} {Unit} ({Frames} frames, render-only {Elapsed})",
            GetActivityName(), result.Rate, RenderBenchmarkHelper.FRAME_UNIT, result.Iterations, result.Elapsed);

        return result;
    }

    #endregion

    #region Tools

    private void ValidateBatchEngine(RenderTaskBatchData batch)
    {
        if (!RequiresMatchingTaskEngine)
            return;

        if (batch.Options.Engine != BenchmarkEngine)
            throw new InvalidOperationException(
                $"{GetActivityName()} requires RenderOptions.Engine={BenchmarkEngine}, but the batch requested {batch.Options.Engine}.");
    }

    private string GetActivityName()
    {
        return AttributeUtils.GetOperatorType<ActivityAttribute>(typeof(TActivity));
    }

    private BlenderRunner GetBlenderRunner()
    {
        if (m_blenderRunner != null)
            return m_blenderRunner;

        var controllerAssemblyPath = typeof(WitControllerRenderModule).Assembly.Location;
        var blenderDir = RenderBinaryResolver.ResolveBlenderRoot(controllerAssemblyPath);

        m_blenderRunner = new BlenderRunner(blenderDir, Logger, TempStorage);

        if (!m_blenderRunner.IsAvailable)
            throw new InvalidOperationException(
                $"Blender not found in controller module at '{blenderDir}'. " +
                "Ensure the render controller module includes the Blender portable installation.");

        return m_blenderRunner;
    }

    private FfmpegRunner GetFfmpegRunner()
    {
        if (m_ffmpegRunner != null)
            return m_ffmpegRunner;

        var controllerAssemblyPath = typeof(WitControllerRenderModule).Assembly.Location;
        var ffmpegDir = RenderBinaryResolver.ResolveFfmpegRoot(controllerAssemblyPath);
        m_ffmpegRunner = new FfmpegRunner(ffmpegDir, Logger, TempStorage);
        if (!m_ffmpegRunner.IsAvailable)
            throw new InvalidOperationException($"ffmpeg not found in controller module at '{ffmpegDir}'. Ensure the render controller module includes the ffmpeg portable installation.");

        return m_ffmpegRunner;
    }

    #endregion

    #region Fields

    private BlenderRunner? m_blenderRunner;

    private FfmpegRunner? m_ffmpegRunner;

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    private IWitTempStorage TempStorage { get; }

    #endregion
}
