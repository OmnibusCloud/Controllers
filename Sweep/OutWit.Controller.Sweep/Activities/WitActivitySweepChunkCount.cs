using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Activities;

/// <summary>
/// Returns the number of chunks in the plan's progressive schedule - the bound of the script's Loop.
/// </summary>
[Activity("Sweep.ChunkCount")]
[MemoryPackable]
public sealed partial class WitActivitySweepChunkCount : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Plan}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySweepChunkCount activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan);
    }

    protected override WitActivitySweepChunkCount InnerClone()
    {
        return new WitActivitySweepChunkCount
        {
            Plan = Plan?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the Plan argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    #endregion
}
