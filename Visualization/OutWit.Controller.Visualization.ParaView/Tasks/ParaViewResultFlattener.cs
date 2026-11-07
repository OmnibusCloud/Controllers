using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Tasks;

/// <summary>
/// Reads a ParaView.Collect / CollectStill "results" parameter as a flat list of
/// <see cref="ParaViewRenderResultData"/>, accepting BOTH shapes: the per-frame
/// ParaViewRenderResultCollection (Grid.ForEach over ParaView.RenderFrame) and the batch
/// ParaViewRenderResultBatchCollection (Grid.ForEach over ParaView.RenderFrameBatch), whose inner
/// results are flattened. The collectors and everything downstream serve both pipelines unchanged;
/// <see cref="ParaViewResultOrdering"/> restores the order from the global task index either way.
/// </summary>
internal static class ParaViewResultFlattener
{
    #region Functions

    /// <summary>
    /// Flattens the results parameter.
    /// </summary>
    /// <param name="pool">The variable pool.</param>
    /// <param name="parameter">The results parameter.</param>
    /// <param name="results">The flat results (nulls preserved for the ordering check).</param>
    /// <returns>True when the parameter held either shape.</returns>
    public static bool TryFlatten(IWitVariablesCollection pool, IWitParameter? parameter, out IReadOnlyList<ParaViewRenderResultData?>? results)
    {
        results = null;

        // The batch shape first: a flat ParaViewRenderResultData collection fails this typed read
        // (its elements do not unwrap to batches), so the per-frame shape is tried next.
        if (pool.TryGetCollection<ParaViewRenderResultBatchData>(parameter, out var batches)
            && batches is { Count: > 0 }
            && batches.All(batch => batch != null))
        {
            results = batches
                .SelectMany(batch => batch!.Results)
                .Select(result => (ParaViewRenderResultData?)result)
                .ToList();
            return true;
        }

        if (pool.TryGetCollection<ParaViewRenderResultData>(parameter, out var flat) && flat != null)
        {
            results = flat;
            return true;
        }

        return false;
    }

    #endregion
}
