using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// One variant in the state's result index: just enough for a document client
/// (the ParaView plugin's cloud-open) to list a sweep's variants and fetch an
/// artifact — the full row (responses, timings, log tails) stays in the
/// manifest blob, which only managed clients read.
/// </summary>
[MemoryPackable]
[JobDocumentContract("sweep.resultIndexEntry@1")]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepResultIndexEntryData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepResultIndexEntryData entry)
            return false;

        return VariantIndex.Is(entry.VariantIndex)
               && Succeeded.Is(entry.Succeeded)
               && FrdBlobId.Is(entry.FrdBlobId)
               && Label.Is(entry.Label);
    }

    public override SweepResultIndexEntryData Clone()
    {
        return new SweepResultIndexEntryData
        {
            VariantIndex = VariantIndex,
            Succeeded = Succeeded,
            FrdBlobId = FrdBlobId,
            Label = Label
        };
    }

    public override string ToString()
    {
        var label = string.IsNullOrEmpty(Label) ? string.Empty : $" ({Label})";
        return $"variant #{VariantIndex}{label}: {(Succeeded ? "done" : "failed")}";
    }

    #endregion

    #region Properties

    /// <summary>Source-table index of the variant.</summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>True when the solver exited 0 and responses were extracted.</summary>
    [MemoryPackOrder(1)]
    public bool Succeeded { get; set; }

    /// <summary>Blob id of the variant's .frd artifact; null when the solve produced none.</summary>
    [MemoryPackOrder(2)]
    public Guid? FrdBlobId { get; set; }

    /// <summary>
    /// Human-readable identity of the variant from the study's parameters ("XMAX=300, T=250"),
    /// so a document client can label it instead of numbering it (audit item #13). Empty for a
    /// deck-set study and for states written before Sweep 0.2.2 - readers fall back to the
    /// number. Appended (order 3): older readers skip it, older writers leave it empty.
    /// </summary>
    [MemoryPackOrder(3)]
    public string Label { get; set; } = string.Empty;

    #endregion
}
