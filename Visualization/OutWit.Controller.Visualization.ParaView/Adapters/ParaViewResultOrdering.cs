using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Adapters;

/// <summary>
/// Collection ordering and completeness (docs 03, sections 11.2 and 13): Grid.ForEach returns results
/// in completion order, so the collectors restore task order and fail on a missing, duplicate or
/// conflicting identity rather than publish a frame set with a hole.
/// </summary>
internal static class ParaViewResultOrdering
{
    #region Functions

    /// <summary>
    /// Orders results by task index and verifies the set is complete and consistent.
    /// </summary>
    /// <param name="results">Results as returned by Grid.ForEach (nulls tolerated, ignored).</param>
    /// <param name="activityName">Activity name for messages.</param>
    /// <returns>The ordered, verified results.</returns>
    /// <exception cref="InvalidOperationException">The set is empty, has gaps, duplicates or conflicting identities.</exception>
    public static IReadOnlyList<ParaViewRenderResultData> Order(IEnumerable<ParaViewRenderResultData?> results, string activityName)
    {
        var ordered = results
            .Where(me => me != null)
            .Select(me => me!)
            .OrderBy(me => me.TaskIndex)
            .ToList();

        if (ordered.Count == 0)
            throw new InvalidOperationException($"{activityName}: no render results to collect.");

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].TaskIndex != i)
            {
                var missing = ordered[i].TaskIndex > i ? $"task index {i} is missing" : $"task index {ordered[i].TaskIndex} appears more than once";
                throw new InvalidOperationException($"{activityName}: the result set is incomplete — {missing} (got {ordered.Count} result(s)).");
            }

            if (ordered[i].ImageBlobId == Guid.Empty)
                throw new InvalidOperationException($"{activityName}: result of task index {i} carries no image blob.");
        }

        var identities = ordered.Select(me => me.TaskId).Where(me => !string.IsNullOrEmpty(me)).ToList();
        if (identities.Distinct(StringComparer.Ordinal).Count() != identities.Count)
            throw new InvalidOperationException($"{activityName}: two results claim the same task identity.");

        return ordered;
    }

    #endregion
}
