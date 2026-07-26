using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

[Activity("Parareal.Collect")]
[MemoryPackable]
public sealed partial class WitActivityPararealCollect : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Plan}, {Wave}, {State}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealCollect activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && Wave.Check(activity.Wave)
               && State.Check(activity.State);
    }

    protected override WitActivityPararealCollect InnerClone()
    {
        return new WitActivityPararealCollect
        {
            Plan = Plan?.Clone() as IWitReference,
            Wave = Wave?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Wave { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
