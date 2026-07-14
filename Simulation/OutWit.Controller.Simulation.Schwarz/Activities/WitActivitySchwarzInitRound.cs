using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

[Activity("Schwarz.InitRound")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzInitRound : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Plan}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySchwarzInitRound activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan);
    }

    protected override WitActivitySchwarzInitRound InnerClone()
    {
        return new WitActivitySchwarzInitRound
        {
            Plan = Plan?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    #endregion
}
