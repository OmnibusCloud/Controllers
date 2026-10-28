using OutWit.Controller.Sweep.Model;

namespace OutWit.Controller.Sweep.Utils;

/// <summary>
/// What lets a sweep continue from a cursor it was handed instead of starting
/// over: the chunk schedule a run goes by, and whether a state fits a plan.
/// The schedule is the state's own when it carries one (Sweep.Model 2.2.0),
/// the plan's otherwise (a cursor an older host wrote), so a resumed run keeps
/// the boundaries its manifest was harvested by even when a fresh plan would
/// open wider or narrower - the fleet is rarely the same width twice.
/// </summary>
public static class SweepResume
{
    #region Functions

    /// <summary>
    /// The chunk schedule a run goes by: the state's when it has one, the plan's otherwise.
    /// </summary>
    /// <param name="plan">The plan of the run.</param>
    /// <param name="state">The cursor of the run.</param>
    /// <returns>Chunk sizes, summing to the variant count.</returns>
    public static IReadOnlyList<int> Sizes(SweepPlanData plan, SweepStateData state)
    {
        return state.ChunkSizes.Count > 0 ? state.ChunkSizes : plan.ChunkSizes;
    }

    /// <summary>
    /// Whether the cursor has moved past the zero state - whether anything was harvested, or
    /// at least attempted, under it.
    /// </summary>
    /// <param name="state">The cursor.</param>
    /// <returns>True when the state is not the zero cursor.</returns>
    public static bool IsStarted(SweepStateData state)
    {
        return state.ChunkIndex > 0 || state.NextVariantOrdinal > 0 || state.ManifestBlobId != null;
    }

    /// <summary>
    /// Everything that keeps a cursor from continuing a plan, each named: a schedule that
    /// does not cover the study's variants, a chunk index outside the schedule, a next ordinal
    /// that is not the sum of the chunks before it, counts that do not add up to what was
    /// harvested, a manifest missing although chunks were harvested. Empty when the state
    /// fits.
    /// </summary>
    /// <param name="plan">The plan the state must fit.</param>
    /// <param name="state">The cursor handed to the run.</param>
    /// <returns>The findings, in words; empty when the state can continue the plan.</returns>
    public static List<string> Findings(SweepPlanData plan, SweepStateData state)
    {
        var findings = new List<string>();
        var sizes = Sizes(plan, state);
        var variants = plan.Options?.Variants.Count ?? 0;
        var covered = sizes.Sum();

        if (covered != variants)
            findings.Add($"the schedule [{string.Join(", ", sizes)}] covers {covered} variant(s), the study has {variants}");

        if (state.ChunkIndex < 0 || state.ChunkIndex > sizes.Count)
        {
            findings.Add($"chunk {state.ChunkIndex} is outside a schedule of {sizes.Count} chunk(s)");
        }
        else
        {
            var before = sizes.Take(state.ChunkIndex).Sum();
            if (state.NextVariantOrdinal != before)
                findings.Add($"the next ordinal {state.NextVariantOrdinal} is not the {before} variant(s) of the {state.ChunkIndex} chunk(s) before it");
        }

        var counted = state.SucceededCount + state.FailedCount + state.RefusedCount;
        if (counted != state.NextVariantOrdinal)
            findings.Add($"the counts ({state.SucceededCount} succeeded, {state.FailedCount} failed, {state.RefusedCount} refused) do not add up to the {state.NextVariantOrdinal} variant(s) harvested");

        if (state.NextVariantOrdinal > 0 && state.ManifestBlobId == null)
            findings.Add("the manifest blob is missing although chunks were harvested");

        return findings;
    }

    #endregion
}
