using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

/// <summary>
/// Server-side stitch of the final wave's owned fields into the result Field blob.
/// Results are re-keyed by SubdomainIndex — the wave arrives in completion order.
/// </summary>
[Activity("Schwarz.Assemble")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzAssemble : WitActivityFunction
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
        if (modelBase is not WitActivitySchwarzAssemble activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && Wave.Check(activity.Wave)
               && State.Check(activity.State);
    }

    protected override WitActivitySchwarzAssemble InnerClone()
    {
        return new WitActivitySchwarzAssemble
        {
            Plan = Plan?.Clone() as IWitReference,
            Wave = Wave?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Reference to the SchwarzPlan; supplies the part count the wave is
    /// validated against before any slice is pasted.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    /// <summary>
    /// Reference to the final SchwarzResultCollection — the emit wave from
    /// Schwarz.MakeFinalTasks; every result must carry a field slice, and a
    /// missing one fails loudly rather than leaving a region zeroed.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Wave { get; init; }

    /// <summary>
    /// Reference to the final SchwarzRound state; its residual history and
    /// convergence verdict are stamped into the assembled Field blob so the
    /// caller can audit the solve without replaying it.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
