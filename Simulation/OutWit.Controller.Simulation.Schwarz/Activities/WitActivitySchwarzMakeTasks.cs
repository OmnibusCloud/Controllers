using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

/// <summary>
/// Server-side fan-out of a regular round: emits one self-contained task per
/// subdomain (boundary traffic only, no field upload) for the Grid.ForEach
/// wave — tasks carry everything whole because the transformer takes a single
/// argument. After round 0 a missing boundary set fails loudly instead of
/// silently solving on a zero band.
/// </summary>
[Activity("Schwarz.MakeTasks")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzMakeTasks : WitActivityFunction
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
        if (modelBase is not WitActivitySchwarzMakeTasks activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State);
    }

    protected override WitActivitySchwarzMakeTasks InnerClone()
    {
        return new WitActivitySchwarzMakeTasks
        {
            Plan = Plan?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Reference to the SchwarzPlan; supplies the subdomain blob handles and
    /// the Dof scalar baked into every task for server-side work estimation.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    /// <summary>
    /// Reference to the current SchwarzRound state; supplies the round number
    /// and each subdomain's incoming boundary blob set.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
