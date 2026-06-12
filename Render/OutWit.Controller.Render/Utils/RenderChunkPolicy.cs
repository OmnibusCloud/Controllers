using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Per-engine persistent-batch chunk sizing used by Render.SplitBatched. The chunk size K trades
/// balance granularity (more, smaller chunks) against scene-load amortisation + benchmark fidelity
/// (fewer, larger chunks). Cycles is render-bound, so it favours MANY SMALL chunks (fine balance,
/// short makespan tail); Eevee/Grease Pencil are overhead-bound (sub-second render), so they need
/// FEW LARGE chunks for the per-chunk scene-load to amortise and the render-only benchmark to become
/// representative. <c>RenderOptions.BatchSize &gt; 0</c> overrides the default; the Blender plugin does
/// not set it, so the per-engine default applies. Constants are starting points to validate on the
/// live fleet.
/// </summary>
public static class RenderChunkPolicy
{
    #region Constants

    // Cycles (render-bound): many small chunks.
    public const int TARGET_CHUNKS_RENDER_BOUND = 48;
    public const int MAX_CHUNK_RENDER_BOUND = 8;

    // Eevee / Grease Pencil (overhead-bound): few large chunks.
    public const int TARGET_CHUNKS_OVERHEAD_BOUND = 8;
    public const int MAX_CHUNK_OVERHEAD_BOUND = 48;

    #endregion

    #region Functions

    /// <summary>
    /// Chunk size for the given engine and total frame count. <paramref name="batchSizeOverride"/>
    /// (RenderOptions.BatchSize) wins when &gt; 0; otherwise the per-engine heuristic
    /// <c>clamp(ceil(frames / TARGET), 1, MAX)</c> applies (floor 1 so small jobs still split).
    /// </summary>
    public static int ComputeChunkSize(RenderEngine engine, int frameCount, int batchSizeOverride)
    {
        if (frameCount <= 0)
            return 1;

        if (batchSizeOverride > 0)
            return Math.Min(batchSizeOverride, frameCount);

        var (target, maxChunk) = engine == RenderEngine.Cycles
            ? (TARGET_CHUNKS_RENDER_BOUND, MAX_CHUNK_RENDER_BOUND)
            : (TARGET_CHUNKS_OVERHEAD_BOUND, MAX_CHUNK_OVERHEAD_BOUND);

        var chunk = (int)Math.Ceiling((double)frameCount / target);
        return Math.Clamp(chunk, 1, maxChunk);
    }

    #endregion
}
