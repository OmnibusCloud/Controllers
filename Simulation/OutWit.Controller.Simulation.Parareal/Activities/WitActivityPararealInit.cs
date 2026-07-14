using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

[Activity("Parareal.Init")]
[MemoryPackable]
public sealed partial class WitActivityPararealInit : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Model}, {Plan}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealInit activity)
            return false;

        return base.Is(activity, tolerance)
               && Model.Check(activity.Model)
               && Plan.Check(activity.Plan);
    }

    protected override WitActivityPararealInit InnerClone()
    {
        return new WitActivityPararealInit
        {
            Model = Model?.Clone() as IWitReference,
            Plan = Plan?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Model { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    #endregion
}
