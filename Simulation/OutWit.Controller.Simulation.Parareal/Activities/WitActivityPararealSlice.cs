using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Activities;

/// <summary>
/// Server-side planning step: validates the model and options before any
/// propagation is scheduled — the kernel build checks time parameters and
/// coarsening divisibility, the options gate bounds iterations and timeline
/// size — and returns the slab plan, last boundary pinned exactly to TotalTime.
/// </summary>
[Activity("Parareal.Slice")]
[MemoryPackable]
public sealed partial class WitActivityPararealSlice : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Model}, {Options}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityPararealSlice activity)
            return false;

        return base.Is(activity, tolerance)
               && Model.Check(activity.Model)
               && Options.Check(activity.Options);
    }

    protected override WitActivityPararealSlice InnerClone()
    {
        return new WitActivityPararealSlice
        {
            Model = Model?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Pool reference to the model blob id the solve is sliced for; recorded
    /// in the plan so later stages never reopen the pool for it.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Model { get; init; }

    /// <summary>
    /// Pool reference to the user-facing solve options; resolved (slabs,
    /// coarsening) and copied into the plan here.
    /// </summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
