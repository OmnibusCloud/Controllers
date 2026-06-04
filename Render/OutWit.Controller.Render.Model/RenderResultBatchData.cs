using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Result of one Render.FrameBatch execution: the per-frame/tile <see cref="RenderResultData"/> for
/// every task in the chunk. Returning a single wrapper model (rather than a raw collection) keeps the
/// Grid.ForEach output a FLAT list of batch results — Render.Collect / Render.CollectTiles then
/// flatten via <c>SelectMany(batch =&gt; batch.Results)</c>, avoiding engine-level nested collections.
/// </summary>
[MemoryPackable]
public partial class RenderResultBatchData : ModelBase
{
    #region Properties

    /// <summary>The per-frame/tile results produced by the chunk's single Blender process.</summary>
    [MemoryPackAllowSerialize]
    public List<RenderResultData> Results { get; set; } = new();

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderResultBatchData other)
            return false;

        if (Results.Count != other.Results.Count)
            return false;

        for (int i = 0; i < Results.Count; i++)
        {
            if (!Results[i].Is(other.Results[i], tolerance))
                return false;
        }

        return true;
    }

    public override ModelBase Clone()
    {
        return new RenderResultBatchData
        {
            Results = Results.Select(result => (RenderResultData)result.Clone()).ToList()
        };
    }

    #endregion
}
