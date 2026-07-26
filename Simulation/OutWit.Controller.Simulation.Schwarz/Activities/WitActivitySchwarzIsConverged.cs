using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

/// <summary>
/// Server-side loop-exit test: reduces the state to the Bool the script's
/// If/Break consumes — relative residual drop below Eps against the round-one
/// anchor, never true before the first completed round. Exists because the
/// .wit grammar cannot compare numbers in-script.
/// </summary>
[Activity("Schwarz.IsConverged")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzIsConverged : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{State}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySchwarzIsConverged activity)
            return false;

        return base.Is(activity, tolerance)
               && State.Check(activity.State);
    }

    protected override WitActivitySchwarzIsConverged InnerClone()
    {
        return new WitActivitySchwarzIsConverged
        {
            State = State?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Reference to the SchwarzRound state written by the latest
    /// Schwarz.Advance; supplies the residual, its initial anchor and Eps.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
