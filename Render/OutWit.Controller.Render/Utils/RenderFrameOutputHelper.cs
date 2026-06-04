using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Shared post-render output handling for the per-frame (Render.Frame) and persistent-batch
/// (Render.FrameBatch) adapters: per-job/task output directory, tile-output normalization
/// (overlap-expanded vs logical vs oversized-crop), <see cref="RenderResultData"/> assembly, and
/// cleanup. Extracted verbatim from the original Render.Frame adapter so both paths stay identical.
/// </summary>
internal static class RenderFrameOutputHelper
{
    #region Constants

    public const int DEFAULT_RESOLUTION_X = 1920;
    public const int DEFAULT_RESOLUTION_Y = 1080;

    #endregion

    #region Output directory

    public static string CreateRenderOutputDirectory(IWitTempStorage tempStorage, Guid jobId, int taskIndex)
    {
        var outputDir = Path.Combine(
            tempStorage.RootPath,
            "witcloud_render",
            jobId.ToString("N"),
            $"task_{taskIndex:D6}");
        Directory.CreateDirectory(outputDir);
        return outputDir;
    }

    public static void CleanupRenderOutput(string outputDir, string? renderedPath)
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

    #region Result

    public static RenderResultData CreateRenderResult(RenderTaskData task, Guid imageBlobId, bool useLogicalTileBounds)
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

    #endregion

    #region Tile normalization

    public static async Task<NormalizedTileOutputData> NormalizeRenderedTileOutputAsync(
        FfmpegRunner ffmpegRunner,
        ILogger logger,
        string activityName,
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

        var imageInfo = await ffmpegRunner.GetImageInfoAsync(renderedPath, cancellationToken);
        if (imageInfo.Width == expectedWidth && imageInfo.Height == expectedHeight)
            return new NormalizedTileOutputData(renderedPath, useLogicalTileBounds: false);

        if (imageInfo.Width == logicalWidth && imageInfo.Height == logicalHeight)
        {
            logger.LogWarning(
                "{ActivityName} tile render output for task {TaskIndex} matched logical tile size {LogicalWidth}x{LogicalHeight} instead of overlap-expanded size {ExpectedWidth}x{ExpectedHeight}; falling back to logical tile bounds",
                activityName, task.TaskIndex, logicalWidth, logicalHeight, expectedWidth, expectedHeight);

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
                    renderedPath, croppedPath, cropOffsetX, cropOffsetY, expectedWidth, expectedHeight, cancellationToken);

                var croppedInfo = await ffmpegRunner.GetImageInfoAsync(croppedPath, cancellationToken);
                if (croppedInfo.Width == expectedWidth && croppedInfo.Height == expectedHeight)
                {
                    logger.LogInformation(
                        "Normalized oversized tile render output to cropped tile size for task {TaskIndex}: {OriginalWidth}x{OriginalHeight} -> {CroppedWidth}x{CroppedHeight} using crop offset {CropOffsetX},{CropOffsetY}",
                        task.TaskIndex, imageInfo.Width, imageInfo.Height, croppedInfo.Width, croppedInfo.Height, cropOffsetX, cropOffsetY);
                    return new NormalizedTileOutputData(croppedPath, useLogicalTileBounds: false);
                }
            }
        }

        throw new InvalidOperationException(
            $"{activityName} tile output size mismatch for task {task.TaskIndex}. Expected {expectedWidth}x{expectedHeight} but got {imageInfo.Width}x{imageInfo.Height}.");
    }

    private static int GetRenderedOffsetX(RenderTaskData task, int width)
        => (int)Math.Round(task.EffectiveRenderMinX * width, MidpointRounding.AwayFromZero);

    private static int GetRenderedOffsetY(RenderTaskData task, int height)
    {
        var renderMaxY = (int)Math.Round(task.EffectiveRenderMaxY * height, MidpointRounding.AwayFromZero);
        return height - renderMaxY;
    }

    private static int GetRenderedWidth(RenderTaskData task, int width)
        => (int)Math.Round((task.EffectiveRenderMaxX - task.EffectiveRenderMinX) * width, MidpointRounding.AwayFromZero);

    private static int GetRenderedHeight(RenderTaskData task, int height)
        => (int)Math.Round((task.EffectiveRenderMaxY - task.EffectiveRenderMinY) * height, MidpointRounding.AwayFromZero);

    private static int GetLogicalWidth(RenderTaskData task, int width)
        => (int)Math.Round((task.TileMaxX - task.TileMinX) * width, MidpointRounding.AwayFromZero);

    private static int GetLogicalHeight(RenderTaskData task, int height)
        => (int)Math.Round((task.TileMaxY - task.TileMinY) * height, MidpointRounding.AwayFromZero);

    #endregion

    #region Nested Types

    public sealed class NormalizedTileOutputData
    {
        public NormalizedTileOutputData(string renderedPath, bool useLogicalTileBounds)
        {
            RenderedPath = renderedPath;
            UseLogicalTileBounds = useLogicalTileBounds;
        }

        public string RenderedPath { get; }

        public bool UseLogicalTileBounds { get; }
    }

    #endregion
}
