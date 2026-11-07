using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// One variant in the state's result index: just enough for a document client
/// (the ParaView plugin's cloud-open) to list a sweep's variants, name them
/// and fetch their artifacts - the full row (responses, timings, logs) stays
/// in the manifest blob, which managed clients read.
/// </summary>
[MemoryPackable]
[JobDocumentContract("sweep.resultIndexEntry@2")]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepResultIndexEntryData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepResultIndexEntryData entry)
            return false;

        return VariantIndex.Is(entry.VariantIndex)
               && Outcome.Is(entry.Outcome)
               && Label.Is(entry.Label)
               && Artifacts.IsSequence(entry.Artifacts, tolerance);
    }

    public override SweepResultIndexEntryData Clone()
    {
        return new SweepResultIndexEntryData
        {
            VariantIndex = VariantIndex,
            Outcome = Outcome,
            Label = Label,
            Artifacts = Artifacts.Select(artifact => artifact.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        var label = string.IsNullOrEmpty(Label) ? string.Empty : $" ({Label})";
        return $"variant #{VariantIndex}{label}: {Outcome}, {Artifacts.Count} artifact(s)";
    }

    #endregion

    #region Properties

    /// <summary>Source-table index of the variant.</summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>The sweep's verdict on the variant.</summary>
    [MemoryPackOrder(1)]
    public SweepOutcome Outcome { get; set; }

    /// <summary>
    /// Human-readable identity of the variant from the study's parameters
    /// ("XMAX=300, T=250"), so a document client can name it instead of
    /// numbering it; empty for a study without parameters (a deck set) -
    /// readers fall back to the number.
    /// </summary>
    [MemoryPackOrder(2)]
    public string Label { get; set; } = string.Empty;

    /// <summary>The variant's downloadable artifacts; empty when it produced none.</summary>
    [MemoryPackOrder(3)]
    public List<SweepArtifactData> Artifacts { get; set; } = [];

    #endregion
}
