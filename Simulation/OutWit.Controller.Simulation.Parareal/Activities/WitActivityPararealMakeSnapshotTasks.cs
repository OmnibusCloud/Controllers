using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

/// <summary>
/// Server-side task builder for the deferred snapshot wave: every slab
/// re-propagates from its converged start state with snapshot emission on —
/// interior output is paid for only once the answer is settled.
/// </summary>
[Activity("Parareal.MakeSnapshotTasks")]
[MemoryPackable]
public sealed partial class WitActivityPararealMakeSnapshotTasks : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Plan}, {State}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealMakeSnapshotTasks activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State);
    }

    protected override WitActivityPararealMakeSnapshotTasks InnerClone()
    {
        return new WitActivityPararealMakeSnapshotTasks
        {
            Plan = Plan?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Pool reference to the slab plan; the time grid, step counts and the
    /// shared model blob id every task carries.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    /// <summary>
    /// Pool reference to the converged iteration state whose slab-boundary
    /// blobs seed the emit wave — all slabs, frontier ignored.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
