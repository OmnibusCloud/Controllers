using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

/// <summary>
/// Server-side task builder for an iteration wave: one propagation task per
/// active slab — after k rounds slabs 0..k−1 are exact and leave the wave.
/// </summary>
[Activity("Parareal.MakeTasks")]
[MemoryPackable]
public sealed partial class WitActivityPararealMakeTasks : WitActivityFunction
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
        if (modelBase is not WitActivityPararealMakeTasks activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State);
    }

    protected override WitActivityPararealMakeTasks InnerClone()
    {
        return new WitActivityPararealMakeTasks
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
    /// Pool reference to the iteration state; supplies the slab-start state
    /// blobs and the frontier that trims the wave.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
