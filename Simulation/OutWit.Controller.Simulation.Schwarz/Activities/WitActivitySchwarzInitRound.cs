using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

/// <summary>
/// Server-side seed of the iteration state: round 0 with an empty boundary
/// set per subdomain (the legitimate zero band) and the convergence threshold
/// copied from the plan — so the first wave solves without imposed boundaries.
/// </summary>
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

    /// <summary>
    /// Reference to the SchwarzPlan produced by Schwarz.Decompose; supplies
    /// the part count and Eps the fresh state is seeded from.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    #endregion
}
