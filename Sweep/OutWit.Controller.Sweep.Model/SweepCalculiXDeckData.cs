using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// One variant's own complete deck in a CalculiX deck-set study (decks meshed
/// elsewhere, no tokens): the uploaded deck and its mesh size for work
/// estimation.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepCalculiXDeckData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepCalculiXDeckData deck)
            return false;

        return VariantIndex.Is(deck.VariantIndex)
               && DeckBlobId.Is(deck.DeckBlobId)
               && NodeCount.Is(deck.NodeCount)
               && ElementCount.Is(deck.ElementCount);
    }

    public override SweepCalculiXDeckData Clone()
    {
        return new SweepCalculiXDeckData
        {
            VariantIndex = VariantIndex,
            DeckBlobId = DeckBlobId,
            NodeCount = NodeCount,
            ElementCount = ElementCount
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: deck {DeckBlobId}, {NodeCount} nodes, {ElementCount} elements";
    }

    #endregion

    #region Properties

    /// <summary>The variant this deck is.</summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>Blob id of the complete deck as uploaded.</summary>
    [MemoryPackOrder(1)]
    public Guid DeckBlobId { get; set; }

    /// <summary>Mesh node count of the deck, for work estimation; 0 falls back to the study's.</summary>
    [MemoryPackOrder(2)]
    public int NodeCount { get; set; }

    /// <summary>Mesh element count of the deck, for work estimation; 0 falls back to the study's.</summary>
    [MemoryPackOrder(3)]
    public int ElementCount { get; set; }

    #endregion
}
