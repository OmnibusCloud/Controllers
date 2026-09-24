using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Activities;

/// <summary>
/// Aggregates a finished chunk's results into the manifest blob and returns the advanced cursor state.
/// </summary>
[Activity("Sweep.Harvest")]
[MemoryPackable]
public sealed partial class WitActivitySweepHarvest : WitActivityFunction
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
        if (modelBase is not WitActivitySweepHarvest activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State)
               && Wave.Check(activity.Wave);
    }

    protected override WitActivitySweepHarvest InnerClone()
    {
        return new WitActivitySweepHarvest
        {
            Plan = Plan?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference,
            Wave = Wave?.Clone() as IWitReference
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

    /// <summary>Reference to the Wave argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Wave { get; init; }

    #endregion
}
