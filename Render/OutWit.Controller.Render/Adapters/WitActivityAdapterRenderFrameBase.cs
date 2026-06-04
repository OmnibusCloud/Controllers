using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Controller.Render.Variables;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal abstract class WitActivityAdapterRenderFrameBase<TActivity> : WitActivityAdapterFunction<TActivity>
    where TActivity : WitActivityFunction, IRenderFrameActivity, new()
{
    #region Constants

    private const int DEFAULT_RESOLUTION_X = 1920;
    private const int DEFAULT_RESOLUTION_Y = 1080;
    private const int DEFAULT_SAMPLES = 128;

    #endregion

    #region Constructors

    protected WitActivityAdapterRenderFrameBase(
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
            throw new ArgumentException($"{GetActivityName()} expects 1 parameter (RenderTask), got {parameters.Length}");

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
        if (!pool.TryGetValue(activity.Task, out RenderTaskData? task) || task == null)
            throw new InvalidOperationException("Failed to get RenderTask parameter 'task'");

        ValidateTaskEngine(task);
        ProcessingManager.ThrowIfCancellationRequested(status.JobId);

        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var blendPath = await BlobService.GetLocalPathAsync(task.SceneBlobId);
        var outputDir = RenderFrameOutputHelper.CreateRenderOutputDirectory(TempStorage, status.JobId, task.TaskIndex);
        string? renderedPath = null;

        try
        {
            var outputBase = Path.Combine(outputDir, "render_");
            var runner = GetBlenderRunner();
            renderedPath = await runner.RenderFrameAsync(
                blendPath, task.Frame, outputBase, task.Options,
                cancellationToken, task);

            var normalizedOutput = await RenderFrameOutputHelper.NormalizeRenderedTileOutputAsync(
                GetFfmpegRunner(), Logger, GetActivityName(), renderedPath, task, outputDir, cancellationToken);
            renderedPath = normalizedOutput.RenderedPath;

            var imageBlobId = await BlobService.UploadFileAsync(renderedPath);
            var result = RenderFrameOutputHelper.CreateRenderResult(task, imageBlobId, normalizedOutput.UseLogicalTileBounds);

            if (!pool.TrySetValue(activity.ReturnReference, result))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for {GetActivityName()}.");
        }
        finally
        {
            RenderFrameOutputHelper.CleanupRenderOutput(outputDir, renderedPath);
        }
    }

    #endregion

    #region Benchmarking

    protected override double EstimateWork(
        TActivity activity,
        IWitVariablesCollection pool)
    {
        if (!pool.TryGetValue(activity.Task, out RenderTaskData? task) || task == null)
            return 1.0;

        var options = task.Options;
        long resX = options.ResolutionX > 0 ? options.ResolutionX : DEFAULT_RESOLUTION_X;
        long resY = options.ResolutionY > 0 ? options.ResolutionY : DEFAULT_RESOLUTION_Y;
        long samples = options.Samples > 0 ? options.Samples : DEFAULT_SAMPLES;
        double tileFraction = (task.TileMaxX - task.TileMinX) * (task.TileMaxY - task.TileMinY);
        return resX * resY * samples * tileFraction;
    }

    public override async Task<IWitBenchmarkResult> RunBenchmark(
        IWitBenchmarkOptions? options,
        CancellationToken cancellationToken)
    {
        var runner = GetBlenderRunner();
        if (!runner.IsAvailable)
            return RenderBenchmarkHelper.CreateUnavailableResult(
                RenderBenchmarkHelper.FRAME_UNIT,
                FrameBenchmarkDatasetId);

        // The render benchmark no longer needs a shipped .blend — it generates a procedural
        // compute-bound scene in-process and times only the render loop (amortising and
        // excluding Blender startup). See @Docs/Active/plan-render-benchmark-redesign.md.
        var result = await RenderBenchmarkHelper.MeasureRenderAsync(
            runner,
            BenchmarkEngine,
            options,
            unit: RenderBenchmarkHelper.FRAME_UNIT,
            datasetId: FrameBenchmarkDatasetId,
            cancellationToken: cancellationToken);

        Logger.LogInformation(
            "{ActivityName} benchmark: {Rate:F0} {Unit} on {Device} ({Frames} frames, render-only {Elapsed})",
            GetActivityName(),
            result.Rate,
            RenderBenchmarkHelper.FRAME_UNIT,
            result.Custom != null && result.Custom.TryGetValue(RenderBenchmarkHelper.CUSTOM_RENDER_DEVICE, out var device) ? device : "unknown",
            result.Iterations,
            result.Elapsed);

        return result;
    }

    #endregion

    #region Tools

    private void ValidateTaskEngine(RenderTaskData task)
    {
        if (!RequiresMatchingTaskEngine)
            return;

        if (task.Options.Engine != BenchmarkEngine)
        {
            throw new InvalidOperationException(
                $"{GetActivityName()} requires RenderOptions.Engine={BenchmarkEngine}, but task {task.TaskIndex} requested {task.Options.Engine}.");
        }
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
