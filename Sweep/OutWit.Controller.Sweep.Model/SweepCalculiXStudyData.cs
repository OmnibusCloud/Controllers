using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// The CalculiX input of a study, in one of two modes: a template (one base
/// deck with baked tokens, one variant per row of values) or a deck set (every
/// variant brings its own complete deck; no tokens, no parameters). The
/// study-wide mesh size, thread policy and extraction request apply to every
/// variant.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepCalculiXStudyData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepCalculiXStudyData study)
            return false;

        return BaseDeckBlobId.Is(study.BaseDeckBlobId)
               && Decks.IsSequence(study.Decks, tolerance)
               && NodeCount.Is(study.NodeCount)
               && ElementCount.Is(study.ElementCount)
               && Threads.Is(study.Threads)
               && Extraction.Check(study.Extraction);
    }

    public override SweepCalculiXStudyData Clone()
    {
        return new SweepCalculiXStudyData
        {
            BaseDeckBlobId = BaseDeckBlobId,
            Decks = Decks.Select(deck => deck.Clone()).ToList(),
            NodeCount = NodeCount,
            ElementCount = ElementCount,
            Threads = Threads,
            Extraction = Extraction?.Clone()
        };
    }

    public override string ToString()
    {
        return Decks.Count > 0
            ? $"CalculiX deck set: {Decks.Count} deck(s)"
            : $"CalculiX template: base deck {BaseDeckBlobId}, {NodeCount} nodes, {ElementCount} elements";
    }

    #endregion

    #region Properties

    /// <summary>Blob id of the base deck with baked tokens (a template study); Empty in a deck set.</summary>
    [MemoryPackOrder(0)]
    public Guid BaseDeckBlobId { get; set; }

    /// <summary>Every variant's own deck (a deck set); empty in a template study.</summary>
    [MemoryPackOrder(1)]
    public List<SweepCalculiXDeckData> Decks { get; set; } = [];

    /// <summary>Mesh node count of the base deck (the same mesh for every variant of a template study).</summary>
    [MemoryPackOrder(2)]
    public int NodeCount { get; set; }

    /// <summary>Mesh element count of the base deck (the same mesh for every variant of a template study).</summary>
    [MemoryPackOrder(3)]
    public int ElementCount { get; set; }

    /// <summary>OMP thread count per solve; 0 = the executing node's cores, capped by the controller.</summary>
    [MemoryPackOrder(4)]
    public int Threads { get; set; }

    /// <summary>Responses to extract per variant; null = the automatic set only.</summary>
    [MemoryPackOrder(5)]
    public CcxExtractionRequestData? Extraction { get; set; }

    #endregion
}
