using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

/// <summary>
/// Server-side iteration step: serial coarse sweep + parareal correction,
/// frontier advance, correction norm, round increment.
/// </summary>
[Activity("Parareal.Correct")]
[MemoryPackable]
public sealed partial class WitActivityPararealCorrect : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Plan}, {State}, {Wave}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealCorrect activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State)
               && Wave.Check(activity.Wave);
    }

    protected override WitActivityPararealCorrect InnerClone()
    {
        return new WitActivityPararealCorrect
        {
            Plan = Plan?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference,
            Wave = Wave?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Pool reference to the slab plan; drives the coarse kernel and the slab
    /// boundaries.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    /// <summary>
    /// Pool reference to the incoming round-k iteration state; the activity
    /// returns the round-(k+1) state as a new value.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    /// <summary>
    /// Pool reference to the fine-propagation results of the active slabs;
    /// arrival order is arbitrary, results are re-keyed by SlabIndex.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Wave { get; init; }

    #endregion
}
