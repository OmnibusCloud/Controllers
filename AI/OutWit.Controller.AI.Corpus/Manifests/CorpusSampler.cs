using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// Picks the shards a run re-executes for integrity: a deterministic share of the tasks (a
/// task is in the sample or not by its index alone, so the same manifest samples the same
/// shards on every run and a lab can reproduce the choice). The replicas go to the allocator
/// in reverse order: a rate-partitioned placement hands the first tasks to the fastest node
/// and the last to the slowest, so the same list in the same order would land every replica
/// next to its primary — reversed, most pairs land apart. The comparison itself is
/// <see cref="Reports.CorpusIntegrityWriter"/>'s, which counts the pairs that still met.
/// </summary>
public static class CorpusSampler
{
    #region Constants

    /// <summary>The seed every sample is drawn with; a fixed constant keeps the sample a function of the index.</summary>
    private const ulong SEED = 0x9E3779B97F4A7C15UL;

    #endregion

    #region Functions

    /// <summary>The sampled share of the tasks, cloned, in reverse index order.</summary>
    /// <param name="tasks">The primary tasks.</param>
    /// <param name="options">The run options; the rate is <see cref="CorpusOptionsData.SamplingRate"/>, clamped to [0, 1].</param>
    /// <returns>The replica tasks; empty when the rate is 0 or nothing was selected.</returns>
    public static List<CorpusTaskData> Sample(IReadOnlyList<CorpusTaskData> tasks, CorpusOptionsData? options)
    {
        var rate = Math.Clamp(options?.SamplingRate ?? 0, 0, 1);
        if (rate <= 0)
            return [];

        return tasks
            .Where(task => IsSelected(task.TaskIndex, rate))
            .OrderByDescending(task => task.TaskIndex)
            .Select(task => task.Clone())
            .ToList();
    }

    /// <summary>Whether a task index is in the sample at a rate: a uniform hash of the index below the rate.</summary>
    /// <param name="taskIndex">The task index.</param>
    /// <param name="rate">The share, in [0, 1].</param>
    /// <returns>True when the task is sampled.</returns>
    public static bool IsSelected(int taskIndex, double rate)
    {
        if (rate >= 1)
            return true;
        if (rate <= 0)
            return false;

        // splitmix64 of the index: well-mixed bits, no pattern over consecutive indices.
        var z = unchecked((ulong)taskIndex + SEED);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;

        var unit = (z >> 11) * (1.0 / (1UL << 53));
        return unit < rate;
    }

    #endregion
}
