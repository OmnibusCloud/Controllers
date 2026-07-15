using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// Chunk result: one verdict per task of the batch, in task order.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyResultBatchData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyResultBatchData other)
            return false;

        if (Results.Count != other.Results.Count)
            return false;

        for (var i = 0; i < Results.Count; i++)
        {
            if (!Results[i].Is(other.Results[i], tolerance))
                return false;
        }

        return true;
    }

    public override VerifyResultBatchData Clone()
    {
        return new VerifyResultBatchData
        {
            Results = Results.Select(r => r.Clone()).ToList()
        };
    }

    #endregion

    #region Properties

    /// <summary>Per-task results, ordered by <see cref="VerifyResultData.TaskIndex"/>.</summary>
    [MemoryPackAllowSerialize]
    [MemoryPackOrder(0)]
    public List<VerifyResultData> Results { get; set; } = [];

    #endregion
}
