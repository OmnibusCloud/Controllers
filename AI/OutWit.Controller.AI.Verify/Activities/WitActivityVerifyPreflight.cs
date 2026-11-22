using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: validate a taskset and report what would be submitted (task/batch counts,
/// runtimes referenced, size estimate, ceiling clamps) before anything runs.
/// </summary>
[Activity("Verify.Preflight")]
[MemoryPackable]
public sealed partial class WitActivityVerifyPreflight : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Taskset}, {Options}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyPreflight activity)
            return false;

        return base.Is(activity, tolerance)
               && Taskset.Check(activity.Taskset)
               && Options.Check(activity.Options);
    }

    protected override WitActivityVerifyPreflight InnerClone()
    {
        return new WitActivityVerifyPreflight
        {
            Taskset = Taskset?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Taskset' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Taskset { get; init; }

    /// <summary>Reference to the 'Options' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
