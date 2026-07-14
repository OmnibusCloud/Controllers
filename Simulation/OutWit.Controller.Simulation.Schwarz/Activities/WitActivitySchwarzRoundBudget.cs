using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

[Activity("Schwarz.RoundBudget")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzRoundBudget : WitActivityFunction
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
        if (modelBase is not WitActivitySchwarzRoundBudget activity)
            return false;

        return base.Is(activity, tolerance)
               && Options.Check(activity.Options);
    }

    protected override WitActivitySchwarzRoundBudget InnerClone()
    {
        return new WitActivitySchwarzRoundBudget
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
