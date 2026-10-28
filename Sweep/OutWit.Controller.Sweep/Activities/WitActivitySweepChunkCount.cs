using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Activities;

/// <summary>
/// Returns the number of chunks left to run - the loop bound of the bundled
/// scripts: every chunk of the plan from a zero cursor, the chunks after the
/// cursor's own from a state the run resumed from, by the run's schedule. The
/// state and the schedule are optional so a script written for Sweep 2.0 -
/// 2.1, which passes the plan alone, still runs.
/// </summary>
[Activity("Sweep.ChunkCount")]
[MemoryPackable]
public sealed partial class WitActivitySweepChunkCount : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return Schedule == null
            ? State == null ? $"{Plan}" : $"{Plan}, {State}"
            : $"{Plan}, {State}, {Schedule}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySweepChunkCount activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State)
               && Schedule.Check(activity.Schedule);
    }

    protected override WitActivitySweepChunkCount InnerClone()
    {
        return new WitActivitySweepChunkCount
        {
            Plan = Plan?.Clone() as IWitReference,
            State = State?.Clone() as IWitReference,
            Schedule = Schedule?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the Plan argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    /// <summary>Reference to the State argument; null in a script that passes the plan alone.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    /// <summary>Reference to the Schedule argument; null in a script without the schedule variable.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Schedule { get; init; }

    #endregion
}
