using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderCollectTiles : WitActivityAdapterFunction<WitActivityRenderCollectTiles>
{
    #region Constructors

    public WitActivityAdapterRenderCollectTiles(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Functions

    protected override WitActivityRenderCollectTiles CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 3)
            throw new ArgumentException($"Render.CollectTiles expects 3 parameters, got {parameters.Length}");

        return new WitActivityRenderCollectTiles
        {
            Results = parameters[0],
            Options = parameters[1],
            TileOptions = parameters[2]
        };
    }

    protected override async Task Process(
        WitActivityRenderCollectTiles activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetCollection(activity.Results, out IReadOnlyList<RenderResultData?>? results) || results == null)
            throw new InvalidOperationException("Failed to get RenderResultCollection parameter 'results'");

        if (!pool.TryGetValue(activity.Options, out RenderOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get RenderOptions parameter 'options'");

        if (!pool.TryGetValue(activity.TileOptions, out TileOptionsData? tileOptions) || tileOptions == null)
            throw new InvalidOperationException("Failed to get TileOptions parameter 'tileOptions'");

        var sorted = results
            .Where(me => me != null)
            .OrderBy(me => me!.Index)
            .Select(me => me!)
            .ToList();

        if (sorted.Count == 0)
            throw new InvalidOperationException("No tile render results to collect");

        var workingDir = Path.Combine(Path.GetTempPath(), "witcloud_render_tiles", status.JobId.ToString("N"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDir);

        try
        {
            var format = options.Format;
            var runner = GetFfmpegRunner();
            var contexts = new List<RenderTileValidationContext>(sorted.Count);

            foreach (var result in sorted)
            {
                var localPath = await BlobService.GetLocalPathAsync(result.ImageBlobId);
                var imageInfo = await runner.GetImageInfoAsync(localPath, ProcessingManager.CancellationToken(status.JobId));
                contexts.Add(new RenderTileValidationContext
                {
                    Result = result,
                    LocalPath = localPath,
                    ImageInfo = imageInfo
                });
            }

            var (outputWidth, outputHeight) = ResolveOutputResolution(options, contexts);

            ValidateOptions(options);
            ValidateTileOptions(tileOptions, outputWidth, outputHeight, tilesX: null, tilesY: null);

            ValidateTileCoverage(contexts, outputWidth, outputHeight, format);

            var placements = contexts
                .Select(me => new RenderTilePlacement
                {
                    InputPath = me.LocalPath,
                    OffsetX = GetTileOffsetX(me.Result, outputWidth),
                    OffsetY = GetTileOffsetY(me.Result, outputHeight),
                    CropX = GetTileCropX(me.Result, outputWidth),
                    CropY = GetTileCropY(me.Result, outputHeight),
                    CropWidth = GetTileCropWidth(me.Result, outputWidth),
                    CropHeight = GetTileCropHeight(me.Result, outputHeight)
                })
                .ToList();

            var outputPath = Path.Combine(workingDir, $"stitched{FormatToExtension(format)}");
            var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
            if (tileOptions.BlendMode == TileBlendMode.AlphaBlend)
            {
                var image = await RenderTileAlphaBlendComposer.ComposeAsync(contexts, outputWidth, outputHeight, runner, cancellationToken);
                await runner.EncodeRgbaImageAsync(image, outputPath, format, cancellationToken);
            }
            else
            {
                await runner.StitchTilesAsync(placements, outputWidth, outputHeight, outputPath, format, cancellationToken);
            }

            var blobId = await BlobService.UploadFileAsync(outputPath);
            if (!pool.TrySetValue(activity.ReturnReference, blobId))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.CollectTiles.");
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
            throw new InvalidOperationException($"ffmpeg not found in controller module at '{ffmpegDir}'. Ensure the render controller module includes the ffmpeg portable installation.");

        return runner;
    }

    private static int GetTileOffsetX(RenderResultData result, int width)
    {
        return (int)Math.Round(result.TileMinX * width, MidpointRounding.AwayFromZero);
    }

    private static int GetTileOffsetY(RenderResultData result, int height)
    {
        var tileMaxY = (int)Math.Round(result.TileMaxY * height, MidpointRounding.AwayFromZero);
        return height - tileMaxY;
    }

    private static int GetRenderedOffsetX(RenderResultData result, int width)
    {
        return (int)Math.Round(result.EffectiveRenderMinX * width, MidpointRounding.AwayFromZero);
    }

    private static int GetRenderedOffsetY(RenderResultData result, int height)
    {
        var renderMaxY = (int)Math.Round(result.EffectiveRenderMaxY * height, MidpointRounding.AwayFromZero);
        return height - renderMaxY;
    }

    private static int GetTileCropX(RenderResultData result, int width)
    {
        return GetTileOffsetX(result, width) - GetRenderedOffsetX(result, width);
    }

    private static int GetTileCropY(RenderResultData result, int height)
    {
        return GetTileOffsetY(result, height) - GetRenderedOffsetY(result, height);
    }

    private static int GetTileCropWidth(RenderResultData result, int width)
    {
        return (int)Math.Round((result.TileMaxX - result.TileMinX) * width, MidpointRounding.AwayFromZero);
    }

    private static int GetTileCropHeight(RenderResultData result, int height)
    {
        return (int)Math.Round((result.TileMaxY - result.TileMinY) * height, MidpointRounding.AwayFromZero);
    }

    private static string FormatToExtension(RenderFormat format)
    {
        return format switch
        {
            RenderFormat.PNG => ".png",
            RenderFormat.JPEG => ".jpg",
            RenderFormat.EXR => ".exr",
            _ => ".png"
        };
    }

    private static void ValidateOptions(RenderOptionsData options)
    {
        if (options.Format == RenderFormat.EXR)
            throw new InvalidOperationException("Render.CollectTiles bootstrap implementation currently supports PNG and JPEG only.");
    }

    private static (int Width, int Height) ResolveOutputResolution(
        RenderOptionsData options,
        IReadOnlyList<RenderTileValidationContext> contexts)
    {
        if (options.ResolutionX > 0 && options.ResolutionY > 0)
            return (options.ResolutionX, options.ResolutionY);

        var inferredWidth = 0;
        var inferredHeight = 0;

        foreach (var context in contexts)
        {
            var renderWidthFraction = context.Result.EffectiveRenderMaxX - context.Result.EffectiveRenderMinX;
            var renderHeightFraction = context.Result.EffectiveRenderMaxY - context.Result.EffectiveRenderMinY;
            if (renderWidthFraction <= 0f || renderHeightFraction <= 0f)
                continue;

            var candidateWidth = (int)Math.Round(context.ImageInfo.Width / renderWidthFraction, MidpointRounding.AwayFromZero);
            var candidateHeight = (int)Math.Round(context.ImageInfo.Height / renderHeightFraction, MidpointRounding.AwayFromZero);

            if (inferredWidth == 0)
                inferredWidth = candidateWidth;
            else if (candidateWidth != inferredWidth)
                throw new InvalidOperationException($"Render.CollectTiles could not infer a consistent output width from tile results. Expected {inferredWidth}, got {candidateWidth} for result index {context.Result.Index}.");

            if (inferredHeight == 0)
                inferredHeight = candidateHeight;
            else if (candidateHeight != inferredHeight)
                throw new InvalidOperationException($"Render.CollectTiles could not infer a consistent output height from tile results. Expected {inferredHeight}, got {candidateHeight} for result index {context.Result.Index}.");
        }

        if (options.ResolutionX <= 0)
            options.ResolutionX = inferredWidth;

        if (options.ResolutionY <= 0)
            options.ResolutionY = inferredHeight;

        if (options.ResolutionX <= 0 || options.ResolutionY <= 0)
            throw new InvalidOperationException("Render.CollectTiles could not resolve the output resolution from render options or tile results.");

        return (options.ResolutionX, options.ResolutionY);
    }

    private static void ValidateTileOptions(TileOptionsData tileOptions, int outputWidth, int outputHeight, int? tilesX, int? tilesY)
    {
        if (tileOptions.OverlapPx < 0)
            throw new InvalidOperationException($"TileOptions.OverlapPx must be >= 0, got {tileOptions.OverlapPx}.");

        if (tileOptions.OverlapPx >= outputWidth || tileOptions.OverlapPx >= outputHeight)
            throw new InvalidOperationException($"TileOptions.OverlapPx must be smaller than the output resolution. Got {tileOptions.OverlapPx}px for output {outputWidth}x{outputHeight}.");

        if (tilesX.HasValue && tilesY.HasValue)
        {
            var coreTileWidth = Math.Max(1, outputWidth / tilesX.Value);
            var coreTileHeight = Math.Max(1, outputHeight / tilesY.Value);
            if (tileOptions.OverlapPx >= coreTileWidth || tileOptions.OverlapPx >= coreTileHeight)
            {
                throw new InvalidOperationException(
                    $"TileOptions.OverlapPx must be smaller than the core tile size. Got {tileOptions.OverlapPx}px for tile size {coreTileWidth}x{coreTileHeight}.");
            }
        }
    }

    private static void ValidateTileCoverage(IReadOnlyList<RenderTileValidationContext> contexts, int outputWidth, int outputHeight, RenderFormat format)
    {
        foreach (var context in contexts)
        {
            ValidateTileBounds(context.Result);
            ValidateTileImageMatchesBounds(context, outputWidth, outputHeight);
            ValidateTileFormat(context, format);
            ValidateTileCropBounds(context, outputWidth, outputHeight);
        }

        var xBoundaries = contexts
            .SelectMany(me => new[] { me.Result.TileMinX, me.Result.TileMaxX })
            .Distinct()
            .OrderBy(me => me)
            .ToArray();
        var yBoundaries = contexts
            .SelectMany(me => new[] { me.Result.TileMinY, me.Result.TileMaxY })
            .Distinct()
            .OrderBy(me => me)
            .ToArray();

        if (xBoundaries.Length < 2 || yBoundaries.Length < 2)
            throw new InvalidOperationException("Render.CollectTiles requires at least one valid tile span in each axis.");

        if (!IsZero(xBoundaries[0]) || !IsOne(xBoundaries[^1]))
            throw new InvalidOperationException("Render.CollectTiles requires tile X coverage to span the full [0..1] range.");

        if (!IsZero(yBoundaries[0]) || !IsOne(yBoundaries[^1]))
            throw new InvalidOperationException("Render.CollectTiles requires tile Y coverage to span the full [0..1] range.");

        var expectedTileCount = (xBoundaries.Length - 1) * (yBoundaries.Length - 1);
        if (contexts.Count != expectedTileCount)
        {
            throw new InvalidOperationException(
                $"Render.CollectTiles requires a complete rectangular tile grid. Expected {expectedTileCount} tiles from the reported bounds, got {contexts.Count}.");
        }

        for (var y = 0; y < yBoundaries.Length - 1; y++)
        {
            for (var x = 0; x < xBoundaries.Length - 1; x++)
            {
                var minX = xBoundaries[x];
                var maxX = xBoundaries[x + 1];
                var minY = yBoundaries[y];
                var maxY = yBoundaries[y + 1];

                var matches = contexts.Count(me =>
                    AreEqual(me.Result.TileMinX, minX)
                    && AreEqual(me.Result.TileMaxX, maxX)
                    && AreEqual(me.Result.TileMinY, minY)
                    && AreEqual(me.Result.TileMaxY, maxY));

                if (matches != 1)
                {
                    throw new InvalidOperationException(
                        $"Render.CollectTiles found invalid tile coverage for cell ({x},{y}) with bounds X[{minX},{maxX}] Y[{minY},{maxY}]. Match count: {matches}.");
                }
            }
        }
    }

    private static void ValidateTileBounds(RenderResultData result)
    {
        if (result.TileMinX < 0f || result.TileMaxX > 1f || result.TileMinY < 0f || result.TileMaxY > 1f)
            throw new InvalidOperationException($"Render.CollectTiles requires normalized tile bounds inside [0..1]. Result index {result.Index} is outside the valid range.");

        if (result.TileMinX >= result.TileMaxX || result.TileMinY >= result.TileMaxY)
            throw new InvalidOperationException($"Render.CollectTiles requires positive tile area. Result index {result.Index} has invalid tile bounds.");

        if (result.EffectiveRenderMinX > result.TileMinX || result.EffectiveRenderMaxX < result.TileMaxX || result.EffectiveRenderMinY > result.TileMinY || result.EffectiveRenderMaxY < result.TileMaxY)
            throw new InvalidOperationException($"Render.CollectTiles requires rendered bounds to fully contain the logical tile bounds. Result index {result.Index} is invalid.");
    }

    private static void ValidateTileImageMatchesBounds(RenderTileValidationContext context, int outputWidth, int outputHeight)
    {
        var expectedWidth = (int)Math.Round((context.Result.EffectiveRenderMaxX - context.Result.EffectiveRenderMinX) * outputWidth, MidpointRounding.AwayFromZero);
        var expectedHeight = (int)Math.Round((context.Result.EffectiveRenderMaxY - context.Result.EffectiveRenderMinY) * outputHeight, MidpointRounding.AwayFromZero);

        if (context.ImageInfo.Width <= 0 || context.ImageInfo.Height <= 0)
        {
            throw new InvalidOperationException(
                $"Render.CollectTiles requires non-empty tile images. Result index {context.Result.Index} resolved to {context.ImageInfo.Width}x{context.ImageInfo.Height}.");
        }

        if (context.ImageInfo.Width != expectedWidth || context.ImageInfo.Height != expectedHeight)
        {
            throw new InvalidOperationException(
                $"Render.CollectTiles tile size mismatch for result index {context.Result.Index}. Expected {expectedWidth}x{expectedHeight} from tile bounds but got {context.ImageInfo.Width}x{context.ImageInfo.Height}.");
        }
    }

    private static void ValidateTileCropBounds(RenderTileValidationContext context, int outputWidth, int outputHeight)
    {
        var cropX = GetTileCropX(context.Result, outputWidth);
        var cropY = GetTileCropY(context.Result, outputHeight);
        var cropWidth = GetTileCropWidth(context.Result, outputWidth);
        var cropHeight = GetTileCropHeight(context.Result, outputHeight);

        if (cropX < 0 || cropY < 0 || cropWidth <= 0 || cropHeight <= 0)
            throw new InvalidOperationException($"Render.CollectTiles produced an invalid crop window for result index {context.Result.Index}.");

        if (cropX + cropWidth > context.ImageInfo.Width || cropY + cropHeight > context.ImageInfo.Height)
        {
            throw new InvalidOperationException(
                $"Render.CollectTiles crop window exceeds rendered tile image bounds for result index {context.Result.Index}. Crop {cropWidth}x{cropHeight}+{cropX}+{cropY}, image {context.ImageInfo.Width}x{context.ImageInfo.Height}.");
        }
    }

    private static void ValidateTileFormat(RenderTileValidationContext context, RenderFormat format)
    {
        var extension = Path.GetExtension(context.LocalPath);
        if (string.IsNullOrWhiteSpace(extension))
            throw new InvalidOperationException($"Render.CollectTiles requires tile files with an extension. Result index {context.Result.Index} had path '{context.LocalPath}'.");

        var expectedExtension = FormatToExtension(format);
        if (!string.Equals(extension, expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Render.CollectTiles expected tile files with extension '{expectedExtension}' for format {format}, but result index {context.Result.Index} used '{extension}'.");
        }
    }

    private static bool AreEqual(float left, float right)
    {
        return Math.Abs(left - right) < 0.0001f;
    }

    private static bool IsOne(float value)
    {
        return AreEqual(value, 1f);
    }

    private static bool IsZero(float value)
    {
        return AreEqual(value, 0f);
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
