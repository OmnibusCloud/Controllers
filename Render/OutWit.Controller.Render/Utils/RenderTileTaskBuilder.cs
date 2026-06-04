using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Pure tile-task geometry shared by <c>Render.SplitTiles</c> (per-tile distribution) and
/// <c>Render.SplitTilesBatched</c> (chunked persistent-batch distribution). Generates the
/// <see cref="RenderTaskData"/> grid for a single frame given the tile counts, render options and
/// tile overlap, and validates the overlap against the resolved output resolution.
/// </summary>
public static class RenderTileTaskBuilder
{
    #region Functions

    public static List<RenderTaskData> BuildTileTasks(
        Guid sceneId,
        int frame,
        int tilesX,
        int tilesY,
        RenderOptionsData options,
        TileOptionsData tileOptions,
        int outputWidth,
        int outputHeight)
    {
        var tasks = new List<RenderTaskData>(tilesX * tilesY);
        var taskIndex = 0;

        for (var y = 0; y < tilesY; y++)
        {
            for (var x = 0; x < tilesX; x++)
            {
                tasks.Add(new RenderTaskData
                {
                    SceneBlobId = sceneId,
                    Frame = frame,
                    TileMinX = x / (float)tilesX,
                    TileMaxX = (x + 1) / (float)tilesX,
                    TileMinY = y / (float)tilesY,
                    TileMaxY = (y + 1) / (float)tilesY,
                    RenderMinX = CalculateRenderMinX(x, tilesX, tileOptions, outputWidth),
                    RenderMaxX = CalculateRenderMaxX(x, tilesX, tileOptions, outputWidth),
                    RenderMinY = CalculateRenderMinY(y, tilesY, tileOptions, outputHeight),
                    RenderMaxY = CalculateRenderMaxY(y, tilesY, tileOptions, outputHeight),
                    TaskIndex = taskIndex++,
                    Options = (RenderOptionsData)options.Clone()
                });
            }
        }

        return tasks;
    }

    public static void ValidateTileOptions(TileOptionsData tileOptions, int outputWidth, int outputHeight, int tilesX, int tilesY)
    {
        if (tileOptions.OverlapPx < 0)
            throw new InvalidOperationException($"TileOptions.OverlapPx must be >= 0, got {tileOptions.OverlapPx}.");

        var coreTileWidth = Math.Max(1, outputWidth / tilesX);
        var coreTileHeight = Math.Max(1, outputHeight / tilesY);
        if (tileOptions.OverlapPx >= coreTileWidth || tileOptions.OverlapPx >= coreTileHeight)
        {
            throw new InvalidOperationException(
                $"TileOptions.OverlapPx must be smaller than the core tile size. Got {tileOptions.OverlapPx}px for tile size {coreTileWidth}x{coreTileHeight}.");
        }
    }

    private static float CalculateRenderMinX(int tileX, int tilesX, TileOptionsData tileOptions, int outputWidth)
    {
        return Math.Max(0f, tileX / (float)tilesX - tileOptions.OverlapPx / (float)outputWidth);
    }

    private static float CalculateRenderMaxX(int tileX, int tilesX, TileOptionsData tileOptions, int outputWidth)
    {
        return Math.Min(1f, (tileX + 1) / (float)tilesX + tileOptions.OverlapPx / (float)outputWidth);
    }

    private static float CalculateRenderMinY(int tileY, int tilesY, TileOptionsData tileOptions, int outputHeight)
    {
        return Math.Max(0f, tileY / (float)tilesY - tileOptions.OverlapPx / (float)outputHeight);
    }

    private static float CalculateRenderMaxY(int tileY, int tilesY, TileOptionsData tileOptions, int outputHeight)
    {
        return Math.Min(1f, (tileY + 1) / (float)tilesY + tileOptions.OverlapPx / (float)outputHeight);
    }

    #endregion
}
