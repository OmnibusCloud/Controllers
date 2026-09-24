using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Activities;

/// <summary>
/// Seals the sweep: returns the final manifest blob id.
/// </summary>
[Activity("Sweep.Finish")]
[MemoryPackable]
public sealed partial class WitActivitySweepFinish : WitActivityFunction
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
        if (modelBase is not WitActivitySweepFinish activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State);
    }

    protected override WitActivitySweepFinish InnerClone()
    {
        return new WitActivitySweepFinish
        {
            Plan = Plan?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the Plan argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    /// <summary>Reference to the State argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
