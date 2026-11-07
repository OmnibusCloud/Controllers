using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Activities;

/// <summary>
/// Materializes the next chunk's variant decks by placeholder substitution and returns their solve tasks.
/// </summary>
[Activity("Sweep.MakeChunk")]
[MemoryPackable]
public sealed partial class WitActivitySweepMakeChunk : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return Schedule == null ? $"{Plan}, {State}" : $"{Plan}, {State}, {Schedule}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySweepMakeChunk activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan)
               && State.Check(activity.State)
               && Schedule.Check(activity.Schedule);
    }

    protected override WitActivitySweepMakeChunk InnerClone()
    {
        return new WitActivitySweepMakeChunk
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

    /// <summary>Reference to the State argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    /// <summary>Reference to the Schedule argument; null in a script without the schedule variable.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Schedule { get; init; }

    #endregion
}
