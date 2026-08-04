using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// One row of the variant table: the substitution values, one per parameter,
/// in the parameter list's order.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepVariantData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepVariantData variant)
            return false;

        return VariantIndex.Is(variant.VariantIndex)
               && Values.Is(variant.Values)
               && DeckBlobId.Is(variant.DeckBlobId)
               && NodeCount.Is(variant.NodeCount)
               && ElementCount.Is(variant.ElementCount);
    }

    public override SweepVariantData Clone()
    {
        return new SweepVariantData
        {
            VariantIndex = VariantIndex,
            Values = [.. Values],
            DeckBlobId = DeckBlobId,
            NodeCount = NodeCount,
            ElementCount = ElementCount
        };
    }

    public override string ToString()
    {
        return DeckBlobId == Guid.Empty
            ? $"variant #{VariantIndex}: [{string.Join(", ", Values)}]"
            : $"variant #{VariantIndex}: own deck {DeckBlobId}";
    }

    #endregion

    #region Properties

    /// <summary>Stable index of the variant in the study.</summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>Substitution values, ordered like the parameter list.</summary>
    [MemoryPackOrder(1)]
    public List<string> Values { get; set; } = [];

    /// <summary>
    /// Blob id of the variant's own complete deck (a deck-set study); Empty
    /// means the variant materializes from the base template. A study mixes
    /// the two modes never — the plan rejects it.
    /// </summary>
    [MemoryPackOrder(2)]
    public Guid DeckBlobId { get; set; }

    /// <summary>
    /// Mesh node count of the variant's own deck, for work estimation; 0
    /// falls back to the study-wide count.
    /// </summary>
    [MemoryPackOrder(3)]
    public int NodeCount { get; set; }

    /// <summary>
    /// Mesh element count of the variant's own deck, for work estimation; 0
    /// falls back to the study-wide count.
    /// </summary>
    [MemoryPackOrder(4)]
    public int ElementCount { get; set; }

    #endregion
}
