using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Reads a Render.Collect / CollectStill / CollectTiles "results" parameter as a flat list of
/// <see cref="RenderResultData"/>, accepting BOTH shapes: the original per-frame
/// <c>RenderResultCollection</c> (each element a <see cref="RenderResultData"/>) and the
/// persistent-batch <c>RenderResultBatchCollection</c> (each element a
/// <see cref="RenderResultBatchData"/> whose inner results are flattened). This lets the collectors
/// serve both Render.Frame and Render.FrameBatch pipelines unchanged downstream.
/// </summary>
public static class RenderResultFlattener
{
    public static bool TryFlatten(
        IWitVariablesCollection pool,
        IWitParameter? parameter,
        out IReadOnlyList<RenderResultData?>? results)
    {
        results = null;

        // Persistent-batch shape first: a RenderResultBatchCollection produced by Grid.ForEach over
        // Render.FrameBatch. A genuinely-flat RenderResultData collection fails this typed read (its
        // elements don't unwrap to RenderResultBatchData), so we fall back to the flat shape below.
        if (pool.TryGetCollection<RenderResultBatchData>(parameter, out var batches)
            && batches is { Count: > 0 }
            && batches.All(batch => batch != null))
        {
            results = batches
                .SelectMany(batch => batch!.Results)
                .Select(result => (RenderResultData?)result)
                .ToList();
            return true;
        }

        // Original per-frame shape: a RenderResultCollection of RenderResultData (one per frame/tile).
        if (pool.TryGetCollection<RenderResultData>(parameter, out var flat) && flat != null)
        {
            results = flat;
            return true;
        }

        return false;
    }
}
