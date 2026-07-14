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

    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Wave { get; init; }

    #endregion
}
