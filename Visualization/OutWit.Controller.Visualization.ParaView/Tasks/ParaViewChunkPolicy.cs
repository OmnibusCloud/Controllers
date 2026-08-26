using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tasks;

/// <summary>
/// Chunk sizing for ParaView.SplitBatched (docs 03, section 27, item 2 — FrameBatch). A ParaView
/// task is startup-bound: a fresh pvpython process costs ~2.5 s of a ~3 s single-frame cycle
/// (section 24.2), so K outputs per process amortise the constant cost K times. The size K trades
/// that amortisation (and the wall time of one chunk) against balance granularity — the LPT
/// allocator spreads chunks over nodes whose rates differ up to ~4× (GPU vs software GL), so a job
/// wants enough chunks for every node to get a fair share and a short makespan tail. The heuristic
/// is <c>clamp(ceil(outputs / TARGET_CHUNKS), 1, MAX_CHUNK)</c> — a small job still splits per
/// output (nothing to amortise, latency wins), a long animation batches up to <see cref="MAX_CHUNK"/>
/// outputs per process. Static by design, like the Render controller's policy: the distributor stays
/// generic and never sizes chunks from node counts or benchmark rates. Constants are starting points
/// to validate on the live fleet.
/// </summary>
public static class ParaViewChunkPolicy
{
    #region Constants

    /// <summary>Chunk count the heuristic aims for — enough for balance on a fleet of a few nodes.</summary>
    public const int TARGET_CHUNKS = 24;

    /// <summary>Upper bound on outputs per process: bounds one chunk's wall time and its attachment union.</summary>
    public const int MAX_CHUNK = 32;

    #endregion

    #region Functions

    /// <summary>
    /// The chunk size for a job of <paramref name="outputCount"/> outputs.
    /// </summary>
    /// <param name="outputCount">Outputs the job renders.</param>
    /// <returns>Outputs per chunk, at least 1 and at most <see cref="MAX_CHUNK"/>.</returns>
    public static int ComputeChunkSize(int outputCount)
    {
        if (outputCount <= 0)
            return 1;

        var chunk = (int)Math.Ceiling((double)outputCount / TARGET_CHUNKS);
        return Math.Clamp(chunk, 1, MAX_CHUNK);
    }

    /// <summary>
    /// Whether adding a task's subset would push a chunk over the per-task byte limit — the
    /// union of a chunk's subsets is what the node materializes, so the limit that guards one
    /// output's subset guards the chunk's union too.
    /// </summary>
    /// <param name="chunkBytes">Bytes the chunk already carries (state included).</param>
    /// <param name="additionalBytes">Bytes the next output would add.</param>
    /// <returns>True when the next output must start a new chunk.</returns>
    public static bool ExceedsSubsetLimit(long chunkBytes, long additionalBytes)
    {
        return chunkBytes + additionalBytes > ParaViewInputLimits.MAX_TASK_SUBSET_BYTES;
    }

    #endregion
}
