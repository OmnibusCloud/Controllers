using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// The sweep's cursor, reassigned once per chunk: which chunk comes next,
/// running totals, the blob id of the manifest holding everything harvested
/// so far, and the per-variant result index. Small by design — a monitoring
/// client polls this variable; managed clients follow the manifest blob for
/// the full rows, document clients (the door renders this type as
/// <c>sweep.state@1</c>) read the index directly.
/// </summary>
[MemoryPackable]
[JobDocumentContract("sweep.state@1")]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepStateData : ModelBase
{
    #region Fields

    private List<SweepResultIndexEntryData>? m_results;

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepStateData state)
            return false;

        return ChunkIndex.Is(state.ChunkIndex)
               && NextVariantOrdinal.Is(state.NextVariantOrdinal)
               && CompletedCount.Is(state.CompletedCount)
               && FailedCount.Is(state.FailedCount)
               && ManifestBlobId.Is(state.ManifestBlobId)
               && Results.IsSequence(state.Results, tolerance);
    }

    public override SweepStateData Clone()
    {
        return new SweepStateData
        {
            ChunkIndex = ChunkIndex,
            NextVariantOrdinal = NextVariantOrdinal,
            CompletedCount = CompletedCount,
            FailedCount = FailedCount,
            ManifestBlobId = ManifestBlobId,
            Results = Results.Select(entry => entry.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"chunk {ChunkIndex}: {CompletedCount} done, {FailedCount} failed";
    }

    #endregion

    #region Properties

    /// <summary>Index of the next chunk to run.</summary>
    [MemoryPackOrder(0)]
    public int ChunkIndex { get; set; }

    /// <summary>Ordinal (position in the variant table) where the next chunk starts.</summary>
    [MemoryPackOrder(1)]
    public int NextVariantOrdinal { get; set; }

    /// <summary>Variants solved successfully so far.</summary>
    [MemoryPackOrder(2)]
    public int CompletedCount { get; set; }

    /// <summary>Variants that finished with a nonzero solver exit so far.</summary>
    [MemoryPackOrder(3)]
    public int FailedCount { get; set; }

    /// <summary>Blob id of the latest harvested manifest; null before the first chunk lands.</summary>
    [MemoryPackOrder(4)]
    public Guid? ManifestBlobId { get; set; }

    /// <summary>
    /// Per-variant result index of everything harvested so far, sorted by
    /// variant — the document-client view of the manifest (appended in the
    /// same harvest that uploads the blob). Null-safe on read: a payload
    /// written before this member existed (Sweep 0.1.x) deserializes with
    /// the member absent, and every reader — Is, Clone, the server's door —
    /// must see an empty index, never a null.
    /// </summary>
    [MemoryPackOrder(5)]
    public List<SweepResultIndexEntryData> Results
    {
        get => m_results ??= [];
        set => m_results = value;
    }

    #endregion
}
