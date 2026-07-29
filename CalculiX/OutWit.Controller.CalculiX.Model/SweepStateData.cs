using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// The sweep's cursor, reassigned once per chunk: which chunk comes next,
/// running totals, and the blob id of the manifest holding everything
/// harvested so far. Small by design — a monitoring client polls this
/// variable and downloads the manifest blob it points at.
/// </summary>
[MemoryPackable]
public sealed partial class SweepStateData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepStateData state)
            return false;

        return ChunkIndex.Is(state.ChunkIndex)
               && NextVariantOrdinal.Is(state.NextVariantOrdinal)
               && CompletedCount.Is(state.CompletedCount)
               && FailedCount.Is(state.FailedCount)
               && ManifestBlobId.Is(state.ManifestBlobId);
    }

    public override SweepStateData Clone()
    {
        return new SweepStateData
        {
            ChunkIndex = ChunkIndex,
            NextVariantOrdinal = NextVariantOrdinal,
            CompletedCount = CompletedCount,
            FailedCount = FailedCount,
            ManifestBlobId = ManifestBlobId
        };
    }

    public override string ToString()
    {
        return $"chunk {ChunkIndex}: {CompletedCount} done, {FailedCount} failed";
    }

    #endregion

    #region Properties

    /// <summary>Index of the next chunk to run.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Ordinal (position in the variant table) where the next chunk starts.</summary>
    public int NextVariantOrdinal { get; set; }

    /// <summary>Variants solved successfully so far.</summary>
    public int CompletedCount { get; set; }

    /// <summary>Variants that finished with a nonzero solver exit so far.</summary>
    public int FailedCount { get; set; }

    /// <summary>Blob id of the latest harvested manifest; null before the first chunk lands.</summary>
    public Guid? ManifestBlobId { get; set; }

    #endregion
}
