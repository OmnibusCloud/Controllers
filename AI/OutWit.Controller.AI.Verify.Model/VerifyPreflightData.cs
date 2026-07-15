using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// Host-side preflight report over a taskset: what will be submitted, whether it is
/// well-formed, which runtimes it references, and rough size estimates — printed to the
/// user BEFORE anything is submitted.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyPreflightData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyPreflightData other)
            return false;

        return WellFormed.Is(other.WellFormed)
               && TaskCount.Is(other.TaskCount)
               && BatchCount.Is(other.BatchCount)
               && RuntimeIds.Is(other.RuntimeIds)
               && UnknownRuntimeIds.Is(other.UnknownRuntimeIds)
               && EstimatedInputBytes.Is(other.EstimatedInputBytes)
               && Messages.Is(other.Messages);
    }

    public override VerifyPreflightData Clone()
    {
        return new VerifyPreflightData
        {
            WellFormed = WellFormed,
            TaskCount = TaskCount,
            BatchCount = BatchCount,
            RuntimeIds = [.. RuntimeIds],
            UnknownRuntimeIds = [.. UnknownRuntimeIds],
            EstimatedInputBytes = EstimatedInputBytes,
            Messages = [.. Messages]
        };
    }

    #endregion

    #region Properties

    /// <summary>True when the taskset parsed and every task is structurally valid.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public bool WellFormed { get; set; }

    [ToString]
    [MemoryPackOrder(1)]
    public int TaskCount { get; set; }

    [ToString]
    [MemoryPackOrder(2)]
    public int BatchCount { get; set; }

    /// <summary>Distinct runtime ids the taskset references.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(3)]
    public List<string> RuntimeIds { get; set; } = [];

    /// <summary>Referenced runtime ids this build does not know how to pin.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(4)]
    public List<string> UnknownRuntimeIds { get; set; } = [];

    /// <summary>Total inline source bytes across the taskset.</summary>
    [ToString]
    [MemoryPackOrder(5)]
    public long EstimatedInputBytes { get; set; }

    /// <summary>Human-readable notes (validation errors, ceiling clamps, warnings).</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(6)]
    public List<string> Messages { get; set; } = [];

    #endregion
}
