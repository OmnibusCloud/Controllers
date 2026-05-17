using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderEncodeVideo : WitActivityAdapterFunction<WitActivityRenderEncodeVideo>
{
    #region Constants

    private const string OUTPUT_FILE_NAME = "render.mp4";

    #endregion

    #region Constructors

    public WitActivityAdapterRenderEncodeVideo(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Functions

    protected override WitActivityRenderEncodeVideo CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException($"Render.EncodeVideo expects 2 parameters, got {parameters.Length}");

        return new WitActivityRenderEncodeVideo
        {
            Frames = parameters[0],
            Options = parameters[1]
        };
    }

    protected override async Task Process(
        WitActivityRenderEncodeVideo activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetObject(activity.Frames, out var framesObject) || framesObject == null)
            throw new InvalidOperationException("Failed to get BlobCollection parameter 'frames'");

        if (!pool.TryGetValue(activity.Options, out VideoOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get VideoOptions parameter 'video'");

        if (framesObject is not IReadOnlyList<Guid?> frameBlobIds)
            throw new InvalidOperationException("Render.EncodeVideo expects BlobCollection to resolve to IReadOnlyList<Guid?>.");

        var orderedBlobIds = frameBlobIds
            .Where(me => me.HasValue)
            .Select(me => me!.Value)
            .ToList();
        if (orderedBlobIds.Count == 0)
            throw new InvalidOperationException("Render.EncodeVideo requires at least one rendered frame blob.");

        ValidateOptions(options);

        var workingDir = Path.Combine(Path.GetTempPath(), "witcloud_render_video", status.JobId.ToString("N"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDir);

        try
        {
            var localPaths = new List<string>(orderedBlobIds.Count);
            foreach (var blobId in orderedBlobIds)
                localPaths.Add(await BlobService.GetLocalPathAsync(blobId));

            var extension = Path.GetExtension(localPaths[0]);
            if (string.IsNullOrWhiteSpace(extension))
                throw new InvalidOperationException("Render.EncodeVideo requires frame files with a valid file extension.");

            if (localPaths.Any(me => !string.Equals(Path.GetExtension(me), extension, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Render.EncodeVideo currently requires all input frames to share the same file extension.");
            }

            for (var index = 0; index < localPaths.Count; index++)
            {
                var destinationPath = Path.Combine(workingDir, $"frame_{index + 1:D4}{extension}");
                File.Copy(localPaths[index], destinationPath, overwrite: true);
            }

            var outputFilePath = Path.Combine(workingDir, OUTPUT_FILE_NAME);
            var inputPattern = Path.Combine(workingDir, $"frame_%04d{extension}");
            await GetFfmpegRunner().EncodeMp4Async(inputPattern, outputFilePath, options, ProcessingManager.CancellationToken(status.JobId));

            var outputBlobId = await BlobService.UploadFileAsync(outputFilePath);
            if (!pool.TrySetValue(activity.ReturnReference, outputBlobId))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.EncodeVideo.");
        }
        finally
        {
            if (Directory.Exists(workingDir))
            {
                try { Directory.Delete(workingDir, recursive: true); }
                catch { }
            }
        }
    }

    private FfmpegRunner GetFfmpegRunner()
    {
        var controllerAssemblyPath = typeof(WitControllerRenderModule).Assembly.Location;
        var ffmpegDir = RenderBinaryResolver.ResolveFfmpegRoot(controllerAssemblyPath);
        var runner = new FfmpegRunner(ffmpegDir, Logger);
        if (!runner.IsAvailable)
        {
            throw new InvalidOperationException(
                $"ffmpeg not found in controller module at '{ffmpegDir}'. Ensure the render controller module includes the ffmpeg portable installation.");
        }

        return runner;
    }

    private static void ValidateOptions(VideoOptionsData options)
    {
        if (options.FrameRate <= 0)
            throw new InvalidOperationException($"VideoOptions.FrameRate must be > 0, got {options.FrameRate}.");

        if (options.ConstantRateFactor is < 0 or > 51)
            throw new InvalidOperationException($"VideoOptions.ConstantRateFactor must be between 0 and 51, got {options.ConstantRateFactor}.");
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
