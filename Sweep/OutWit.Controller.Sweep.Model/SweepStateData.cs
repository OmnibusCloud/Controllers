using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// The sweep's cursor, reassigned once per chunk: which chunk comes next,
/// running totals by outcome, the blob id of the manifest holding everything
/// harvested so far, and the per-variant result index. Small by design - a
/// monitoring client polls this variable; managed clients follow the manifest
/// blob for the full rows, document clients (the door renders this type as
/// <c>sweep.state@2</c>) read the index directly.
/// </summary>
[MemoryPackable]
[JobDocumentContract("sweep.state@2")]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepStateData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepStateData state)
            return false;

        return ChunkIndex.Is(state.ChunkIndex)
               && NextVariantOrdinal.Is(state.NextVariantOrdinal)
               && SucceededCount.Is(state.SucceededCount)
               && FailedCount.Is(state.FailedCount)
               && RefusedCount.Is(state.RefusedCount)
               && ManifestBlobId.Is(state.ManifestBlobId)
               && Results.IsSequence(state.Results, tolerance);
    }

    public override SweepStateData Clone()
    {
        return new SweepStateData
        {
            ChunkIndex = ChunkIndex,
            NextVariantOrdinal = NextVariantOrdinal,
            SucceededCount = SucceededCount,
            FailedCount = FailedCount,
            RefusedCount = RefusedCount,
            ManifestBlobId = ManifestBlobId,
            Results = Results.Select(entry => entry.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"chunk {ChunkIndex}: {SucceededCount} succeeded, {FailedCount} failed, {RefusedCount} refused";
    }

    #endregion

    #region Properties

    /// <summary>Index of the next chunk to run.</summary>
    [MemoryPackOrder(0)]
    public int ChunkIndex { get; set; }

    /// <summary>Ordinal (position in the variant table) where the next chunk starts.</summary>
    [MemoryPackOrder(1)]
    public int NextVariantOrdinal { get; set; }

    /// <summary>Variants that finished cleanly so far.</summary>
    [MemoryPackOrder(2)]
    public int SucceededCount { get; set; }

    /// <summary>Variants that ran and did not finish cleanly so far.</summary>
    [MemoryPackOrder(3)]
    public int FailedCount { get; set; }

    /// <summary>Variants a node refused before anything ran, so far.</summary>
    [MemoryPackOrder(4)]
    public int RefusedCount { get; set; }

    /// <summary>Blob id of the latest harvested manifest; null before the first chunk lands.</summary>
    [MemoryPackOrder(5)]
    public Guid? ManifestBlobId { get; set; }

    /// <summary>Per-variant result index of everything harvested so far, sorted by variant.</summary>
    [MemoryPackOrder(6)]
    public List<SweepResultIndexEntryData> Results { get; set; } = [];

    #endregion
}
