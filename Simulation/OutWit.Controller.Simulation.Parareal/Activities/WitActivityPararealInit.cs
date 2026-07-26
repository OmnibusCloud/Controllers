using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

/// <summary>
/// Server-side round 0: serial coarse sweep from the initial field, one
/// uploaded state blob per slab boundary, and the problem scale ‖U⁰‖ that
/// anchors the relative stopping criterion for the rest of the solve.
/// </summary>
[Activity("Parareal.Init")]
[MemoryPackable]
public sealed partial class WitActivityPararealInit : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Model}, {Plan}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealInit activity)
            return false;

        return base.Is(activity, tolerance)
               && Model.Check(activity.Model)
               && Plan.Check(activity.Plan);
    }

    protected override WitActivityPararealInit InnerClone()
    {
        return new WitActivityPararealInit
        {
            Model = Model?.Clone() as IWitReference,
            Plan = Plan?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Pool reference to the model blob id; must match the plan's model blob —
    /// a mismatch fails fast here rather than mid-wave.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Model { get; init; }

    /// <summary>
    /// Pool reference to the slab plan produced by Parareal.Slice.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Plan { get; init; }

    #endregion
}
