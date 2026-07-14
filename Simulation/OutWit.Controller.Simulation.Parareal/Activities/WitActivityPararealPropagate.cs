using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

/// <summary>
/// Node-side slab propagation — a pure function of its task (PR-8): retries and
/// reassignment re-run it safely because outputs are new immutable blobs.
/// </summary>
[Activity("Parareal.Propagate")]
[CanRunInParallelOnClient(false)]
[RequiresResources(MinRamMb = 4096)]
[MemoryPackable]
public sealed partial class WitActivityPararealPropagate : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Task}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealPropagate activity)
            return false;

        return base.Is(activity, tolerance)
               && Task.Check(activity.Task);
    }

    protected override WitActivityPararealPropagate InnerClone()
    {
        return new WitActivityPararealPropagate
        {
            Task = Task?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Task { get; init; }

    #endregion
}
