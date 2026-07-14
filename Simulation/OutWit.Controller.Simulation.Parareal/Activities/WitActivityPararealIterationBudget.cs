using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

[Activity("Parareal.IterationBudget")]
[MemoryPackable]
public sealed partial class WitActivityPararealIterationBudget : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Options}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealIterationBudget activity)
            return false;

        return base.Is(activity, tolerance)
               && Options.Check(activity.Options);
    }

    protected override WitActivityPararealIterationBudget InnerClone()
    {
        return new WitActivityPararealIterationBudget
        {
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
