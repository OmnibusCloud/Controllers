using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

/// <summary>
/// Extracts the iteration budget from the options as the Loop bound — the
/// .wit grammar has no property access, so the read lives in an activity.
/// </summary>
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

    /// <summary>
    /// Pool reference to the solve options; MaxIterations becomes the loop
    /// bound.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
