using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// What a lab learns before anything runs: how many shards, whether every origin is allowed,
/// which sources exceed the cap, how much traffic the contributors will carry, and every
/// problem with the manifest by line.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusPreflightData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusPreflightData other)
            return false;

        return WellFormed.Is(other.WellFormed)
               && TaskCount.Is(other.TaskCount)
               && RefusedOrigins.Is(other.RefusedOrigins)
               && OversizedSources.Is(other.OversizedSources)
               && EstimatedTrafficBytes.Is(other.EstimatedTrafficBytes)
               && Messages.Is(other.Messages);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusPreflightData Clone()
    {
        return new CorpusPreflightData
        {
            WellFormed = WellFormed,
            TaskCount = TaskCount,
            RefusedOrigins = [.. RefusedOrigins],
            OversizedSources = OversizedSources,
            EstimatedTrafficBytes = EstimatedTrafficBytes,
            Messages = [.. Messages]
        };
    }

    #endregion

    #region Properties

    /// <summary>Whether every manifest line parsed and every source is allowed and within the cap.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public bool WellFormed { get; set; }

    /// <summary>Shards that parsed.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int TaskCount { get; set; }

    /// <summary>Hosts the manifest names that are not on the allowlist.</summary>
    [MemoryPackOrder(2)]
    public List<string> RefusedOrigins { get; set; } = [];

    /// <summary>Sources whose estimated size exceeds the cap.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int OversizedSources { get; set; }

    /// <summary>Sum of the sources' estimated sizes: what the contributors will fetch.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public long EstimatedTrafficBytes { get; set; }

    /// <summary>Every problem, by manifest line.</summary>
    [MemoryPackOrder(5)]
    public List<string> Messages { get; set; } = [];

    #endregion
}
