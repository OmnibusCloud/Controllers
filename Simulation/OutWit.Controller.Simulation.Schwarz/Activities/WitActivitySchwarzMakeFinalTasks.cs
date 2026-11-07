using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

/// <summary>
/// Server-side fan-out of the post-loop wave: same tasks as Schwarz.MakeTasks
/// but with field emission on — nodes back-substitute from their cached
/// factorizations and upload the owned field slices Schwarz.Assemble stitches
/// into the result.
/// </summary>
[Activity("Schwarz.MakeFinalTasks")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzMakeFinalTasks : WitActivityFunction
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
        if (modelBase is not WitActivitySchwarzMakeFinalTasks activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State);
    }

    protected override WitActivitySchwarzMakeFinalTasks InnerClone()
    {
        return new WitActivitySchwarzMakeFinalTasks
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
    /// Reference to the converged (or budget-exhausted) SchwarzRound state;
    /// the final wave re-imposes its boundary sets so the emitted fields match
    /// the last iterate.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
