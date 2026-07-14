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

    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Wave { get; init; }

    #endregion
}
