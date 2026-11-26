using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// Host-side: empties a collection of the result packages a fan-out returned, once the run is
/// settled and its documents are written (<c>solved = Smt.Forget(solved);</c>). Such a collection
/// holds the results in the order the platform kept them, one node's after another's, each with
/// the ticket of its package and the id of its blob: what the documents hold back. A finished
/// job's variables can be read by its submitter, so the script leaves them empty.
/// </summary>
[Activity("Smt.Forget")]
[MemoryPackable]
public sealed partial class WitActivitySmtForget : WitActivityFunction
{
    #region Functions

    /// <summary>The activity as a script names it.</summary>
    /// <returns>The references it is given.</returns>
    protected override string InnerString()
    {
        return string.Join(", ", new object?[] { Results }.OfType<object>());
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySmtForget activity)
            return false;

        return base.Is(activity, tolerance)
               && Results.Check(activity.Results);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtForget InnerClone()
    {
        return new WitActivitySmtForget
        {
            Results = Results?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Results' argument of the activity: the variable to empty, an <c>SmtResultPackageCollection</c>, which the call is assigned back to.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Results { get; init; }

    #endregion
}
