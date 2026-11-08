using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// Job-level knobs: the byte cap per shard (the contributor's traffic is a granted resource),
/// how many shards a node may fetch at once, and the share of tasks re-executed for integrity.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusOptionsData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusOptionsData other)
            return false;

        return MaxShardBytes.Is(other.MaxShardBytes)
               && FetchConcurrency.Is(other.FetchConcurrency)
               && SamplingRate.Is(other.SamplingRate, tolerance);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusOptionsData Clone()
    {
        return new CorpusOptionsData { MaxShardBytes = MaxShardBytes, FetchConcurrency = FetchConcurrency, SamplingRate = SamplingRate };
    }

    #endregion

    #region Properties

    /// <summary>Most bytes a shard may be on the wire (0 = the controller's cap, 1 GiB).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public long MaxShardBytes { get; set; }

    /// <summary>Shards a node fetches at once (0 = 1).</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int FetchConcurrency { get; set; }

    /// <summary>Share of tasks re-executed on another node for integrity, 0 to 1.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public double SamplingRate { get; set; }

    #endregion
}
