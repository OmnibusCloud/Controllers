using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Activities;

/// <summary>
/// Creates the zero cursor state the chunk loop advances.
/// </summary>
[Activity("Sweep.InitState")]
[MemoryPackable]
public sealed partial class WitActivitySweepInitState : WitActivityFunction
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
        if (modelBase is not WitActivitySweepInitState activity)
            return false;

        return base.Is(activity, tolerance)
               && Plan.Check(activity.Plan);
    }

    protected override WitActivitySweepInitState InnerClone()
    {
        return new WitActivitySweepInitState
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
