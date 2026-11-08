using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// One node task: one shard through one pipeline. The task carries the origins it may fetch
/// from and the byte cap, re-checked on the node before any request — the node never
/// fetches what the task did not declare.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusTaskData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusTaskData other)
            return false;

        return TaskIndex.Is(other.TaskIndex)
               && Source.Check(other.Source)
               && Pipeline.Check(other.Pipeline)
               && AllowedOrigins.Is(other.AllowedOrigins)
               && MaxShardBytes.Is(other.MaxShardBytes);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusTaskData Clone()
    {
        return new CorpusTaskData
        {
            TaskIndex = TaskIndex,
            Source = Source?.Clone(),
            Pipeline = Pipeline?.Clone(),
            AllowedOrigins = [.. AllowedOrigins],
            MaxShardBytes = MaxShardBytes
        };
    }

    #endregion

    #region Properties

    /// <summary>The task's index in the manifest and the report.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int TaskIndex { get; set; }

    /// <summary>The shard to fetch.</summary>
    [MemoryPackOrder(1)]
    public CorpusSourceData? Source { get; set; }

    /// <summary>The pipeline to run it through.</summary>
    [MemoryPackOrder(2)]
    public CorpusPipelineData? Pipeline { get; set; }

    /// <summary>Host names the node may fetch from (exact, case-insensitive); the source's host must be one of them.</summary>
    [MemoryPackOrder(3)]
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>Most bytes the node will accept for the shard; past it the fetch stops and the task is Oversized.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public long MaxShardBytes { get; set; }

    #endregion
}
