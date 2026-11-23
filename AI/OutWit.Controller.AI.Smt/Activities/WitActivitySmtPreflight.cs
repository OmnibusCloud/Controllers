using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// Host-side: what a problem set and a run's options come to before anything is sent
/// (<c>SmtPreflight</c>): the set counted, the options checked, the budget as numbers, the upper
/// bound, the memory of one session and the packages a run would make. One pass over the set,
/// nothing kept.
/// </summary>
[Activity("Smt.Preflight")]
[MemoryPackable]
public sealed partial class WitActivitySmtPreflight : WitActivityFunction
{
    #region Functions

    /// <summary>The activity as a script names it.</summary>
    /// <returns>The references it is given.</returns>
    protected override string InnerString()
    {
        return string.Join(", ", new object?[] { Problems, Options }.OfType<object>());
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySmtPreflight activity)
            return false;

        return base.Is(activity, tolerance)
               && Problems.Check(activity.Problems)
               && Options.Check(activity.Options);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtPreflight InnerClone()
    {
        return new WitActivitySmtPreflight
        {
            Problems = Problems?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Problems' argument of the activity: the problem set: a <c>Blob</c> of JSON lines.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Problems { get; init; }

    /// <summary>Reference to the 'Options' argument of the activity: the run's options: an <c>SmtOptions</c>.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
