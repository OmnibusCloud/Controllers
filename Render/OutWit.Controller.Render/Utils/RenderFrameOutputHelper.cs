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

        // Boundary-based sizes (RenderTileGeometry): the stitcher places and crops tiles by rounded
        // BOUNDARIES, so the expected size here must be the boundary difference — a rounded width
        // disagrees by ±1 exactly at fractional boundaries and rejects healthy tiles.
        var expectedWidth = RenderTileGeometry.Span(task.EffectiveRenderMinX, task.EffectiveRenderMaxX, outputWidth);
        var expectedHeight = RenderTileGeometry.Span(task.EffectiveRenderMinY, task.EffectiveRenderMaxY, outputHeight);
        var logicalWidth = RenderTileGeometry.Span(task.TileMinX, task.TileMaxX, outputWidth);
        var logicalHeight = RenderTileGeometry.Span(task.TileMinY, task.TileMaxY, outputHeight);

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

        // Blender snaps interior border edges a hair differently per build (the local build lands on
        // our rounded boundary; the farm build truncated one edge → a 1px-short tile the old code
        // rejected). Re-anchor a tile whose size matches the boundary-truncation model to the rounded
        // grid by compensating the EXACT edge each boundary snapped on (see below). Only interior
        // (overlap-bearing) edges snap — frame edges (fraction 0/1) are exact, so a discrepancy there
        // is a real error handled by the oversized-crop path or the loud throw.
        var reanchored = await TryReanchorTileToBoundaryGridAsync(
            ffmpegRunner, logger, activityName, renderedPath, task, outputDir,
            imageInfo, outputWidth, outputHeight, expectedWidth, expectedHeight, cancellationToken);
        if (reanchored != null)
            return reanchored;

        var widthPadding = imageInfo.Width - outputWidth;
        var heightPadding = imageInfo.Height - outputHeight;
        if (widthPadding >= 0 && heightPadding >= 0 && widthPadding % 2 == 0 && heightPadding % 2 == 0)
        {
            var paddingX = widthPadding / 2;
            var paddingY = heightPadding / 2;
            var cropOffsetX = RenderTileGeometry.BoundaryPixel(task.EffectiveRenderMinX, outputWidth) + paddingX;
            var cropOffsetY = RenderTileGeometry.TopOffset(task.EffectiveRenderMaxY, outputHeight) + paddingY;

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
            $"{activityName} tile output size mismatch for task {task.TaskIndex}. Expected {expectedWidth}x{expectedHeight} " +
            $"(Blender-snapped would be {RenderTileGeometry.BlenderSpan(task.EffectiveRenderMinX, task.EffectiveRenderMaxX, outputWidth)}x" +
            $"{RenderTileGeometry.BlenderSpan(task.EffectiveRenderMinY, task.EffectiveRenderMaxY, outputHeight)}) " +
            $"but got {imageInfo.Width}x{imageInfo.Height}.");
    }

    private static async Task<NormalizedTileOutputData?> TryReanchorTileToBoundaryGridAsync(
        FfmpegRunner ffmpegRunner,
        ILogger logger,
        string activityName,
        string renderedPath,
        RenderTaskData task,
        string outputDir,
        RenderImageInfo imageInfo,
        int outputWidth,
        int outputHeight,
        int expectedWidth,
        int expectedHeight,
        CancellationToken cancellationToken)
    {
        if (expectedWidth <= 0 || expectedHeight <= 0)
            return null;

        // A border render lands off the rounded boundary grid only when Blender's build TRUNCATES a
        // boundary the round grid rounds up (floor vs round, ±1 per boundary — proven by the farm
        // incident). Which physical EDGE moved is fully determined by the geometry, not by the size
        // delta: compute the snap of each of the four boundaries independently and compensate that
        // exact edge. A boundary that snaps outward (round > Blender's truncation) means a MISSING
        // row/column on that side → pad it; the retained content then sits on the rounded grid at its
        // true position (the earlier right/bottom-only scheme mis-compensated max-Y and min-X snaps,
        // shifting the whole tile 1px). Fractions are bottom-based (Blender border): min-X = left
        // edge, max-X = right, min-Y = BOTTOM, max-Y = TOP.
        var snapLeft = SnapPixels(task.EffectiveRenderMinX, outputWidth);   // extra columns Blender rendered on the left
        var snapRight = SnapPixels(task.EffectiveRenderMaxX, outputWidth);  // columns Blender omitted on the right
        var snapBottom = SnapPixels(task.EffectiveRenderMinY, outputHeight);
        var snapTop = SnapPixels(task.EffectiveRenderMaxY, outputHeight);

        // The re-anchor only explains the boundary-truncation class: the actual image must be exactly
        // the Blender-truncated size (round size minus the outward snaps plus the inward ones). Any
        // other size is a different error → fall through to the oversized-crop path or a loud throw.
        var predictedWidth = expectedWidth - snapRight + snapLeft;
        var predictedHeight = expectedHeight - snapTop + snapBottom;
        if (imageInfo.Width != predictedWidth || imageInfo.Height != predictedHeight)
            return null;

        // Belt-and-braces: a genuine snap is at most MAX_EDGE_SNAP_PX per edge (float noise on the
        // product); anything larger is not the rounding class.
        if (snapLeft > RenderTileGeometry.MAX_EDGE_SNAP_PX || snapRight > RenderTileGeometry.MAX_EDGE_SNAP_PX
            || snapTop > RenderTileGeometry.MAX_EDGE_SNAP_PX || snapBottom > RenderTileGeometry.MAX_EDGE_SNAP_PX)
            return null;

        if (snapLeft == 0 && snapRight == 0 && snapTop == 0 && snapBottom == 0)
            return null;

        // Crop the edges Blender rendered wide (inward snaps), pad the edges it rendered short
        // (outward snaps). Every padded edge is a frame-interior overlap boundary the stitch discards.
        var normalizedPath = Path.Combine(outputDir, $"tile_snap{Path.GetExtension(renderedPath)}");
        await ffmpegRunner.NormalizeTileGeometryAsync(
            renderedPath, normalizedPath,
            cropLeft: snapLeft, cropTop: 0, cropRight: 0, cropBottom: snapBottom,
            padLeft: 0, padTop: snapTop, padRight: snapRight, padBottom: 0,
            expectedWidth, expectedHeight, cancellationToken);

        var normalizedInfo = await ffmpegRunner.GetImageInfoAsync(normalizedPath, cancellationToken);
        if (normalizedInfo.Width != expectedWidth || normalizedInfo.Height != expectedHeight)
            return null;

        logger.LogInformation(
            "{ActivityName} re-anchored a Blender-snapped tile for task {TaskIndex}: {ActualWidth}x{ActualHeight} -> {ExpectedWidth}x{ExpectedHeight} (snap left={SnapLeft}, right={SnapRight}, top={SnapTop}, bottom={SnapBottom})",
            activityName, task.TaskIndex, imageInfo.Width, imageInfo.Height, expectedWidth, expectedHeight,
            snapLeft, snapRight, snapTop, snapBottom);

        return new NormalizedTileOutputData(normalizedPath, useLogicalTileBounds: false);
    }

    /// <summary>
    /// Pixels a boundary snaps by between the rounded grid and Blender's own truncation of the same
    /// fraction (0 or 1 in practice). A nonzero value on a given boundary means the retained tile is
    /// short one row/column on that edge.
    /// </summary>
    private static int SnapPixels(float fraction, int dimension)
        => RenderTileGeometry.BoundaryPixel(fraction, dimension) - RenderTileGeometry.BlenderBoundaryPixel(fraction, dimension);

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
