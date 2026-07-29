using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// The validated, immutable execution plan of one sweep: the base deck, the
/// options as submitted, and the chunk schedule computed up front. Mutable
/// progress lives in <see cref="SweepStateData"/>, never here.
/// </summary>
[MemoryPackable]
public sealed partial class SweepPlanData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepPlanData plan)
            return false;

        return BaseDeckBlobId.Is(plan.BaseDeckBlobId)
               && Options.Check(plan.Options)
               && ChunkSizes.Is(plan.ChunkSizes);
    }

    public override SweepPlanData Clone()
    {
        return new SweepPlanData
        {
            BaseDeckBlobId = BaseDeckBlobId,
            Options = Options?.Clone(),
            ChunkSizes = [.. ChunkSizes]
        };
    }

    public override string ToString()
    {
        return $"plan: {Options?.Variants.Count ?? 0} variant(s) in {ChunkSizes.Count} chunk(s)";
    }

    #endregion

    #region Properties

    /// <summary>Blob id of the base deck with baked placeholder tokens.</summary>
    public Guid BaseDeckBlobId { get; set; }

    /// <summary>The study as submitted.</summary>
    public SweepOptionsData? Options { get; set; }

    /// <summary>Progressive chunk sizes; sums to the variant count.</summary>
    public List<int> ChunkSizes { get; set; } = [];

    #endregion
}
