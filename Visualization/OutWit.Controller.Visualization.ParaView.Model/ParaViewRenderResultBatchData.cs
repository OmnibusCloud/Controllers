using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Result of one ParaView.RenderFrameBatch execution: the per-output
/// <see cref="ParaViewRenderResultData"/> of every task in the chunk. A wrapper model rather than a
/// raw collection keeps the Grid.ForEach output a FLAT list of batch results; ParaView.Collect /
/// CollectStill flatten it (and still accept the per-frame shape). Model 0.5.0.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewRenderResultBatchData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewRenderResultBatchData other)
            return false;

        return Results.Count == other.Results.Count
               && Results.Zip(other.Results, (left, right) => left.Is(right, tolerance)).All(me => me);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewRenderResultBatchData
        {
            Results = [.. Results.Select(me => (ParaViewRenderResultData)me.Clone())]
        };
    }

    #endregion

    #region Properties

    /// <summary>The per-output results of the chunk's single pvpython process, in render order.</summary>
    [MemoryPackOrder(0)]
    public List<ParaViewRenderResultData> Results { get; set; } = [];

    #endregion
}
