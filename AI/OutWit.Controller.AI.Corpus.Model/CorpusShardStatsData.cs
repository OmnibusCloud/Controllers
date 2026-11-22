using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// What happened to one shard: records in, documents out, where the rest went (by rule), the
/// language histogram, bytes moved and time spent. The per-shard observability of a run and
/// the raw material of the aggregate report; the counts are a function of the shard and the
/// pipeline, the times are not.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusShardStatsData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusShardStatsData other)
            return false;

        if (Drops.Count != other.Drops.Count || Languages.Count != other.Languages.Count || UnsupportedLanguages.Count != other.UnsupportedLanguages.Count)
            return false;

        if ((Pdf == null) != (other.Pdf == null) || (Pdf != null && !Pdf.Is(other.Pdf!, tolerance)))
            return false;

        for (var i = 0; i < Drops.Count; i++)
        {
            if (!Drops[i].Is(other.Drops[i], tolerance))
                return false;
        }

        for (var i = 0; i < Languages.Count; i++)
        {
            if (!Languages[i].Is(other.Languages[i], tolerance))
                return false;
        }

        for (var i = 0; i < UnsupportedLanguages.Count; i++)
        {
            if (!UnsupportedLanguages[i].Is(other.UnsupportedLanguages[i], tolerance))
                return false;
        }

        return RecordsIn.Is(other.RecordsIn)
               && DocumentsExtracted.Is(other.DocumentsExtracted)
               && DocumentsOut.Is(other.DocumentsOut)
               && BytesFetched.Is(other.BytesFetched)
               && BytesProduced.Is(other.BytesProduced)
               && FetchMs.Is(other.FetchMs)
               && ProcessMs.Is(other.ProcessMs)
               && WaitMs.Is(other.WaitMs);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusShardStatsData Clone()
    {
        return new CorpusShardStatsData
        {
            RecordsIn = RecordsIn,
            DocumentsExtracted = DocumentsExtracted,
            DocumentsOut = DocumentsOut,
            Drops = Drops.Select(d => d.Clone()).ToList(),
            Languages = Languages.Select(l => l.Clone()).ToList(),
            BytesFetched = BytesFetched,
            BytesProduced = BytesProduced,
            FetchMs = FetchMs,
            ProcessMs = ProcessMs,
            WaitMs = WaitMs,
            UnsupportedLanguages = UnsupportedLanguages.Select(u => u.Clone()).ToList(),
            Pdf = Pdf?.Clone()
        };
    }

    #endregion

    #region Properties

    /// <summary>Records read from the shard (every type).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int RecordsIn { get; set; }

    /// <summary>Documents that yielded text.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int DocumentsExtracted { get; set; }

    /// <summary>Documents in the product.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public int DocumentsOut { get; set; }

    /// <summary>Documents dropped, by rule name, in rule-name order.</summary>
    [MemoryPackOrder(3)]
    public List<CorpusCountData> Drops { get; set; } = [];

    /// <summary>Documents in the product by language code, in code order.</summary>
    [MemoryPackOrder(4)]
    public List<CorpusCountData> Languages { get; set; } = [];

    /// <summary>Bytes fetched from the origin.</summary>
    [ToString]
    [MemoryPackOrder(5)]
    public long BytesFetched { get; set; }

    /// <summary>Bytes of the compressed product.</summary>
    [ToString]
    [MemoryPackOrder(6)]
    public long BytesProduced { get; set; }

    /// <summary>Wall time of the fetch.</summary>
    [MemoryPackOrder(7)]
    public int FetchMs { get; set; }

    /// <summary>Wall time of the processing.</summary>
    [MemoryPackOrder(8)]
    public int ProcessMs { get; set; }

    /// <summary>Wall time the shard waited on the node for a download slot and a processing slot (the node's own queue; not part of the fetch or the processing times).</summary>
    [MemoryPackOrder(9)]
    public int WaitMs { get; set; }

    /// <summary>
    /// The documents dropped as <c>language_unsupported</c>, by where they were nearest: a
    /// language the pack knows and does not support (its code), <c>past_limit:</c> and a supported
    /// language's code (past that language's limit), or <c>no_profile</c> (no n-gram in common
    /// with any profile). Names in ordinal order; the counts sum to the drop's.
    /// </summary>
    [MemoryPackOrder(10)]
    public List<CorpusCountData> UnsupportedLanguages { get; set; } = [];

    /// <summary>What the PDF stage did with the shard's PDFs; null when it met none (a WET shard, a WARC shard without a PDF, the stage off).</summary>
    [MemoryPackOrder(11)]
    public CorpusPdfStatsData? Pdf { get; set; }

    #endregion
}
