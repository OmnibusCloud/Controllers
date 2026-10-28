using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Activities;

/// <summary>
/// Creates the zero cursor state the chunk loop advances and writes the run's
/// chunk schedule into the schedule variable - or, handed the cursor of an
/// earlier run (and its schedule), keeps them once they fit the plan.
/// </summary>
[Activity("Sweep.InitState")]
[MemoryPackable]
public sealed partial class WitActivitySweepInitState : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return Schedule == null ? $"{Plan}" : $"{Plan}, {Schedule}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySweepInitState activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && Schedule.Check(activity.Schedule);
    }

    protected override WitActivitySweepInitState InnerClone()
    {
        return new WitActivitySweepInitState
        {
            Plan = Plan?.Clone() as IWitReference,
            Schedule = Schedule?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the Plan argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    /// <summary>Reference to the Schedule variable the activity writes; null in a script that declares none (Sweep 2.0 - 2.1).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Schedule { get; init; }

    #endregion
}
