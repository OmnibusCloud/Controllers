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
        return $"{Plan}, {Wave}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySchwarzAssemble activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && Wave.Check(activity.Wave);
    }

    protected override WitActivitySchwarzAssemble InnerClone()
    {
        return new WitActivitySchwarzAssemble
        {
            Plan = Plan?.Clone() as IWitReference,
            Wave = Wave?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Wave { get; init; }

    #endregion
}
