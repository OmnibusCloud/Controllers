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
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
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
        var outputDir = CreateRenderOutputDirectory(status.JobId, task.TaskIndex);
        string? renderedPath = null;

        try
        {
            var outputBase = Path.Combine(outputDir, "render_");
            var runner = GetBlenderRunner();
            renderedPath = await runner.RenderFrameAsync(
                blendPath, task.Frame, outputBase, task.Options,
                cancellationToken, task);

            var normalizedOutput = await NormalizeRenderedTileOutputAsync(renderedPath, task, outputDir, cancellationToken);
            renderedPath = normalizedOutput.RenderedPath;

            var imageBlobId = await BlobService.UploadFileAsync(renderedPath);
            var result = CreateRenderResult(task, imageBlobId, normalizedOutput.UseLogicalTileBounds);

            if (!pool.TrySetValue(activity.ReturnReference, result))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for {GetActivityName()}.");
        }
        finally
        {
            CleanupRenderOutput(outputDir, renderedPath);
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

        var benchmarkBlend = FindBenchmarkScene();
        if (benchmarkBlend == null)
        {
            Logger.LogWarning("Benchmark scene not found — returning zero rate for {ActivityName}", GetActivityName());
            return RenderBenchmarkHelper.CreateUnavailableResult(
                RenderBenchmarkHelper.FRAME_UNIT,
                FrameBenchmarkDatasetId);
        }

        var renderOptions = RenderBenchmarkHelper.CreateBenchmarkRenderOptions(BenchmarkEngine);
        var outputDir = Path.Combine(Path.GetTempPath(), $"witcloud_benchmark_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try
        {
            double totalPixels = renderOptions.ResolutionX * renderOptions.ResolutionY * renderOptions.Samples;
            var invocationIndex = 0;

            var result = await RenderBenchmarkHelper.MeasureAsync(
                options,
                unit: RenderBenchmarkHelper.FRAME_UNIT,
                datasetId: FrameBenchmarkDatasetId,
                action: async ct =>
                {
                    invocationIndex++;
                    var outputBase = Path.Combine(outputDir, $"bench_{invocationIndex:D4}_");
                    await runner.RenderFrameAsync(benchmarkBlend, 1, outputBase, renderOptions, ct);
                },
                cancellationToken: cancellationToken,
                rateFactory: (iterations, elapsed) => elapsed.TotalSeconds > 0
                    ? totalPixels * iterations / elapsed.TotalSeconds
                    : 0,
                maxIterations: 3);

            Logger.LogInformation("{ActivityName} benchmark: {Rate:F0} {Unit} ({Elapsed})",
                GetActivityName(),
                result.Rate,
                RenderBenchmarkHelper.FRAME_UNIT,
                result.Elapsed);

            return result;
        }
        finally
        {
            try { Directory.Delete(outputDir, recursive: true); }
            catch { }
        }
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

        m_blenderRunner = new BlenderRunner(blenderDir, Logger);

        if (!m_blenderRunner.IsAvailable)
            throw new InvalidOperationException(
                $"Blender not found in controller module at '{blenderDir}'. " +
                "Ensure the render controller module includes the Blender portable installation.");

        return m_blenderRunner;
    }

    private static string? FindBenchmarkScene()
    {
        return RenderBenchmarkHelper.FindBenchmarkScene();
    }

    private async Task<NormalizedTileOutputData> NormalizeRenderedTileOutputAsync(
        string renderedPath,
        RenderTaskData task,
        string outputDir,
        CancellationToken cancellationToken)
    {
        if (task.IsFullFrame)
            return new NormalizedTileOutputData(renderedPath, useLogicalTileBounds: false);

        var outputWidth = task.Options.ResolutionX > 0 ? task.Options.ResolutionX : DEFAULT_RESOLUTION_X;
        var outputHeight = task.Options.ResolutionY > 0 ? task.Options.ResolutionY : DEFAULT_RESOLUTION_Y;
        var expectedWidth = GetRenderedWidth(task, outputWidth);
        var expectedHeight = GetRenderedHeight(task, outputHeight);
        var logicalWidth = GetLogicalWidth(task, outputWidth);
        var logicalHeight = GetLogicalHeight(task, outputHeight);

        var ffmpegRunner = GetFfmpegRunner();
        var imageInfo = await ffmpegRunner.GetImageInfoAsync(renderedPath, cancellationToken);
        if (imageInfo.Width == expectedWidth && imageInfo.Height == expectedHeight)
            return new NormalizedTileOutputData(renderedPath, useLogicalTileBounds: false);

        if (imageInfo.Width == logicalWidth && imageInfo.Height == logicalHeight)
        {
            Logger.LogWarning(
                "{ActivityName} tile render output for task {TaskIndex} matched logical tile size {LogicalWidth}x{LogicalHeight} instead of overlap-expanded size {ExpectedWidth}x{ExpectedHeight}; falling back to logical tile bounds",
                GetActivityName(),
                task.TaskIndex,
                logicalWidth,
                logicalHeight,
                expectedWidth,
                expectedHeight);

            return new NormalizedTileOutputData(renderedPath, useLogicalTileBounds: true);
        }

        var widthPadding = imageInfo.Width - outputWidth;
        var heightPadding = imageInfo.Height - outputHeight;
        if (widthPadding >= 0 && heightPadding >= 0 && widthPadding % 2 == 0 && heightPadding % 2 == 0)
        {
            var paddingX = widthPadding / 2;
            var paddingY = heightPadding / 2;
            var cropOffsetX = GetRenderedOffsetX(task, outputWidth) + paddingX;
            var cropOffsetY = GetRenderedOffsetY(task, outputHeight) + paddingY;

            if (cropOffsetX >= 0
                && cropOffsetY >= 0
                && cropOffsetX + expectedWidth <= imageInfo.Width
                && cropOffsetY + expectedHeight <= imageInfo.Height)
            {
                var croppedPath = Path.Combine(outputDir, $"tile_crop{Path.GetExtension(renderedPath)}");
                await ffmpegRunner.CropImageAsync(
                    renderedPath,
                    croppedPath,
                    cropOffsetX,
                    cropOffsetY,
                    expectedWidth,
                    expectedHeight,
                    cancellationToken);

                var croppedInfo = await ffmpegRunner.GetImageInfoAsync(croppedPath, cancellationToken);
                if (croppedInfo.Width == expectedWidth && croppedInfo.Height == expectedHeight)
                {
                    Logger.LogInformation(
                        "Normalized oversized tile render output to cropped tile size for task {TaskIndex}: {OriginalWidth}x{OriginalHeight} -> {CroppedWidth}x{CroppedHeight} using crop offset {CropOffsetX},{CropOffsetY}",
                        task.TaskIndex,
                        imageInfo.Width,
                        imageInfo.Height,
                        croppedInfo.Width,
                        croppedInfo.Height,
                        cropOffsetX,
                        cropOffsetY);
                    return new NormalizedTileOutputData(croppedPath, useLogicalTileBounds: false);
                }
            }
        }

        throw new InvalidOperationException(
            $"{GetActivityName()} tile output size mismatch for task {task.TaskIndex}. Expected {expectedWidth}x{expectedHeight} but got {imageInfo.Width}x{imageInfo.Height}.");
    }

    private FfmpegRunner GetFfmpegRunner()
    {
        if (m_ffmpegRunner != null)
            return m_ffmpegRunner;

        var controllerAssemblyPath = typeof(WitControllerRenderModule).Assembly.Location;
        var ffmpegDir = RenderBinaryResolver.ResolveFfmpegRoot(controllerAssemblyPath);
        m_ffmpegRunner = new FfmpegRunner(ffmpegDir, Logger);
        if (!m_ffmpegRunner.IsAvailable)
            throw new InvalidOperationException($"ffmpeg not found in controller module at '{ffmpegDir}'. Ensure the render controller module includes the ffmpeg portable installation.");

        return m_ffmpegRunner;
    }

    private static int GetRenderedOffsetX(RenderTaskData task, int width)
    {
        return (int)Math.Round(task.EffectiveRenderMinX * width, MidpointRounding.AwayFromZero);
    }

    private static int GetRenderedOffsetY(RenderTaskData task, int height)
    {
        var renderMaxY = (int)Math.Round(task.EffectiveRenderMaxY * height, MidpointRounding.AwayFromZero);
        return height - renderMaxY;
    }

    private static int GetRenderedWidth(RenderTaskData task, int width)
    {
        return (int)Math.Round((task.EffectiveRenderMaxX - task.EffectiveRenderMinX) * width, MidpointRounding.AwayFromZero);
    }

    private static int GetRenderedHeight(RenderTaskData task, int height)
    {
        return (int)Math.Round((task.EffectiveRenderMaxY - task.EffectiveRenderMinY) * height, MidpointRounding.AwayFromZero);
    }

    private static string CreateRenderOutputDirectory(Guid jobId, int taskIndex)
    {
        var outputDir = Path.Combine(
            Path.GetTempPath(),
            "witcloud_render",
            jobId.ToString("N"),
            $"task_{taskIndex:D6}");
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    private static RenderResultData CreateRenderResult(RenderTaskData task, Guid imageBlobId, bool useLogicalTileBounds)
    {
        var renderMinX = useLogicalTileBounds ? task.TileMinX : task.RenderMinX;
        var renderMaxX = useLogicalTileBounds ? task.TileMaxX : task.RenderMaxX;
        var renderMinY = useLogicalTileBounds ? task.TileMinY : task.RenderMinY;
        var renderMaxY = useLogicalTileBounds ? task.TileMaxY : task.RenderMaxY;

        return new RenderResultData
        {
            Index = task.TaskIndex,
            ImageBlobId = imageBlobId,
            TileMinX = task.TileMinX,
            TileMaxX = task.TileMaxX,
            TileMinY = task.TileMinY,
            TileMaxY = task.TileMaxY,
            RenderMinX = renderMinX,
            RenderMaxX = renderMaxX,
            RenderMinY = renderMinY,
            RenderMaxY = renderMaxY
        };
    }

    private static int GetLogicalWidth(RenderTaskData task, int width)
    {
        return (int)Math.Round((task.TileMaxX - task.TileMinX) * width, MidpointRounding.AwayFromZero);
    }

    private static int GetLogicalHeight(RenderTaskData task, int height)
    {
        return (int)Math.Round((task.TileMaxY - task.TileMinY) * height, MidpointRounding.AwayFromZero);
    }

    private static void CleanupRenderOutput(string outputDir, string? renderedPath)
    {
        if (!string.IsNullOrWhiteSpace(renderedPath) && File.Exists(renderedPath))
        {
            try { File.Delete(renderedPath); }
            catch { }
        }

        if (Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); }
            catch { }
        }
    }

    #endregion

    #region Fields

    private BlenderRunner? m_blenderRunner;

    private FfmpegRunner? m_ffmpegRunner;

    #endregion

    #region Nested Types

    private sealed class NormalizedTileOutputData
    {
        #region Constructors

        public NormalizedTileOutputData(string renderedPath, bool useLogicalTileBounds)
        {
            RenderedPath = renderedPath;
            UseLogicalTileBounds = useLogicalTileBounds;
        }

        #endregion

        #region Properties

        public string RenderedPath { get; }

        public bool UseLogicalTileBounds { get; }

        #endregion
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
