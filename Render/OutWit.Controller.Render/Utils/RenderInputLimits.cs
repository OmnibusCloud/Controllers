namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Guards on job-sized inputs so a single malformed or hostile submission cannot exhaust host
/// memory before any work is dispatched. The splitters materialize one task per frame / per tile and
/// the alpha-blend compositor allocates managed pixel planes sized by the requested resolution — all
/// three are driven by numbers that arrive from the client, so each has a generous ceiling that no
/// legitimate render approaches. Every limit throws a clear <see cref="InvalidOperationException"/>
/// naming the offending value; nothing is silently clamped.
/// </summary>
internal static class RenderInputLimits
{
    #region Constants

    /// <summary>
    /// Upper bound on frames materialized by a single Render.Split(Batched). A 200 000-frame job is
    /// ~2.3 hours at 24 fps — far beyond any realistic distributed render, but it caps the task-list
    /// allocation instead of letting an end/start range near int.MaxValue OOM the host.
    /// </summary>
    public const int MAX_FRAMES_PER_SPLIT = 200_000;

    /// <summary>
    /// Upper bound on the total tile count of a single tiled split. Real tile grids are a handful per
    /// axis; 4096 (e.g. 64×64) is enormous, and the cap stops tilesX·tilesY from expanding into a
    /// billion-task allocation when overlap is zero (which lets any grid pass the core-tile check).
    /// </summary>
    public const int MAX_TILES_PER_SPLIT = 4_096;

    /// <summary>
    /// Upper bound on the pixel count the in-memory AlphaBlend compositor may allocate (it holds five
    /// double planes + an RGBA byte buffer, ~44 bytes/pixel). 64 megapixels (~8K) bounds the compose
    /// to well under 3 GB; larger canvases must use the streaming CenterPriorityCrop mode.
    /// </summary>
    public const long MAX_ALPHA_BLEND_PIXELS = 64_000_000L;

    #endregion

    #region Functions

    public static void ValidateFrameRange(int startFrame, int endFrame)
    {
        var count = (long)endFrame - startFrame + 1L;
        if (count > MAX_FRAMES_PER_SPLIT)
            throw new InvalidOperationException(
                $"Render split frame range [{startFrame}, {endFrame}] requests {count} frames, exceeding the {MAX_FRAMES_PER_SPLIT}-frame limit for a single job.");
    }

    public static void ValidateTileGrid(int tilesX, int tilesY)
    {
        var total = (long)tilesX * tilesY;
        if (total > MAX_TILES_PER_SPLIT)
            throw new InvalidOperationException(
                $"Render tile grid {tilesX}x{tilesY} requests {total} tiles, exceeding the {MAX_TILES_PER_SPLIT}-tile limit for a single job.");
    }

    public static void ValidateAlphaBlendResolution(int outputWidth, int outputHeight)
    {
        var pixels = (long)outputWidth * outputHeight;
        if (pixels > MAX_ALPHA_BLEND_PIXELS)
            throw new InvalidOperationException(
                $"AlphaBlend tile compositing for a {outputWidth}x{outputHeight} canvas needs {pixels} pixels of managed buffers, exceeding the {MAX_ALPHA_BLEND_PIXELS}-pixel limit. Use the CenterPriorityCrop blend mode for canvases this large.");
    }

    #endregion
}
