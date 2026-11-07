using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

/// <summary>
/// Server-side round step: fixed-order residual reduction, boundary routing,
/// coarse correction and the round increment — everything the .wit grammar
/// cannot express in-script (no arithmetic, no property access).
/// </summary>
[Activity("Schwarz.Advance")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzAdvance : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Plan}, {State}, {Wave}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySchwarzAdvance activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State)
               && Wave.Check(activity.Wave);
    }

    protected override WitActivitySchwarzAdvance InnerClone()
    {
        return new WitActivitySchwarzAdvance
        {
            Plan = Plan?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference,
            Wave = Wave?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Reference to the SchwarzPlan; its routing graph decides which
    /// producers' boundary blobs feed each subdomain in the next round.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    /// <summary>
    /// Reference to the SchwarzRound state entering the round; supplies the
    /// counter, residual anchors and history the successor state is derived
    /// from (the state itself is never mutated in place).
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    /// <summary>
    /// Reference to the SchwarzResultCollection gathered by the Grid.ForEach
    /// wave — completion-ordered on arrival; re-keyed by SubdomainIndex and
    /// validated as an exact permutation of the current round before reduction.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Wave { get; init; }

    #endregion
}
