using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// One fetchable input: a public URL the node reads the shard from (Pattern U), its format,
/// and what the manifest knew about it — a size estimate for the traffic line and a published
/// checksum when the origin gives one.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusSourceData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusSourceData other)
            return false;

        return Url.Is(other.Url)
               && Format.Is(other.Format)
               && SizeBytes.Is(other.SizeBytes)
               && Sha256.Is(other.Sha256);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusSourceData Clone()
    {
        return new CorpusSourceData { Url = Url, Format = Format, SizeBytes = SizeBytes, Sha256 = Sha256 };
    }

    #endregion

    #region Properties

    /// <summary>The shard's URL at the public origin.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string Url { get; set; } = "";

    /// <summary>What the file holds.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public CorpusSourceFormat Format { get; set; }

    /// <summary>Size on the wire as the manifest estimates it (0 = unknown).</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public long SizeBytes { get; set; }

    /// <summary>Lower-case hex SHA-256 the origin publishes for the file, or "" when none.</summary>
    [MemoryPackOrder(3)]
    public string Sha256 { get; set; } = "";

    #endregion
}
