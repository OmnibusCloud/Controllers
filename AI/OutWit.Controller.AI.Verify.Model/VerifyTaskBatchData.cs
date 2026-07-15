using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// A chunk of tasks sharing one runtime, executed by a SINGLE Verify.ExecuteBatch
/// activity: the runtime module is compiled once and every task runs in its own fresh
/// store, in parallel across the node's cores. The persistent-batch economics that
/// fixed render balance, applied to sandboxed execution.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyTaskBatchData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyTaskBatchData other)
            return false;

        if (Tasks.Count != other.Tasks.Count)
            return false;

        for (var i = 0; i < Tasks.Count; i++)
        {
            if (!Tasks[i].Is(other.Tasks[i], tolerance))
                return false;
        }

        return RuntimeId.Is(other.RuntimeId)
               && DefaultLimits.Check(other.DefaultLimits);
    }

    public override VerifyTaskBatchData Clone()
    {
        return new VerifyTaskBatchData
        {
            RuntimeId = RuntimeId,
            DefaultLimits = DefaultLimits?.Clone(),
            Tasks = Tasks.Select(t => t.Clone()).ToList()
        };
    }

    #endregion

    #region Properties

    /// <summary>Runtime shared by every task in the chunk (chunking is by runtime affinity).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string RuntimeId { get; set; } = "";

    /// <summary>Limit defaults applied to every task that does not override them.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(1)]
    public VerifyLimitsData? DefaultLimits { get; set; }

    /// <summary>The tasks executed together on one node, in submit order.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(2)]
    public List<VerifyTaskData> Tasks { get; set; } = [];

    #endregion
}
