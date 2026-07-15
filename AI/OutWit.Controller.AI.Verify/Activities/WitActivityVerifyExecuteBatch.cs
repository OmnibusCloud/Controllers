using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// The primitive: a node executes a whole batch of sandboxed tasks. Pure function of its
/// input — retries and reassignment re-run it safely because a task's output is
/// deterministic. One per client: the batch itself saturates the node's cores.
/// </summary>
[Activity("Verify.ExecuteBatch")]
[CanRunInParallelOnClient(false)]
[MemoryPackable]
public sealed partial class WitActivityVerifyExecuteBatch : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Batch}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyExecuteBatch activity)
            return false;

        return base.Is(activity, tolerance)
               && Batch.Check(activity.Batch);
    }

    protected override WitActivityVerifyExecuteBatch InnerClone()
    {
        return new WitActivityVerifyExecuteBatch
        {
            Batch = Batch?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Batch { get; init; }

    #endregion
}
