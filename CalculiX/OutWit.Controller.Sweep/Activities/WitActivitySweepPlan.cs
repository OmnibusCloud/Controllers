using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Activities;

/// <summary>
/// Validates the submitted study against the base deck and computes the immutable execution plan, chunk schedule included.
/// </summary>
[Activity("Sweep.Plan")]
[MemoryPackable]
public sealed partial class WitActivitySweepPlan : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{BaseDeck}, {Options}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySweepPlan activity)
            return false;

        return base.Is(activity, tolerance)
               && BaseDeck.Check(activity.BaseDeck)
               && Options.Check(activity.Options);
    }

    protected override WitActivitySweepPlan InnerClone()
    {
        return new WitActivitySweepPlan
        {
            BaseDeck = BaseDeck?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the BaseDeck argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? BaseDeck { get; init; }

    /// <summary>Reference to the Options argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
