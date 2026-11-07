using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Picks the tasks a run re-executes for integrity: a deterministic share of the taskset
/// (a task is in the sample or not by its index alone, so the same taskset samples the same
/// tasks on every run and a lab can reproduce the choice), re-planned into fresh batches the
/// allocator places independently of the primaries. The comparison itself is
/// <see cref="VerifyIntegrityWriter"/>'s.
/// </summary>
public static class VerifySampler
{
    #region Constants

    /// <summary>The seed every sample is drawn with; a fixed constant keeps the sample a function of the index.</summary>
    private const ulong SEED = 0x9E3779B97F4A7C15UL;

    #endregion

    #region Functions

    /// <summary>
    /// Re-plans the sampled share of the batches' tasks into replica batches.
    /// </summary>
    /// <param name="batches">The primary batches (the tasks as the nodes ran them, limits resolved).</param>
    /// <param name="options">The run options; the rate is <see cref="VerifyOptionsData.SamplingRate"/>, clamped to [0, 1].</param>
    /// <returns>The replica batches; empty when the rate is 0 or nothing was selected.</returns>
    public static List<VerifyTaskBatchData> Sample(IReadOnlyList<VerifyTaskBatchData> batches, VerifyOptionsData? options)
    {
        var rate = Math.Clamp(options?.SamplingRate ?? 0, 0, 1);
        if (rate <= 0)
            return [];

        var selected = batches
            .SelectMany(batch => batch.Tasks)
            .Where(task => IsSelected(task.TaskIndex, rate))
            .OrderBy(task => task.TaskIndex)
            .Select(task => task.Clone())
            .ToList();

        return selected.Count == 0 ? [] : VerifyTasksetPlanner.Plan(selected, options).Batches;
    }

    /// <summary>
    /// Whether a task index is in the sample at a rate: a uniform hash of the index below the rate.
    /// </summary>
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
