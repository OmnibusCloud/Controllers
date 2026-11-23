using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// Host-side: the integrity document of a settled run, as a JSON blob
/// (<c>docs/smt-integrity.schema.json</c>): what the host did with what the nodes returned, in counts.
/// </summary>
[Activity("Smt.Integrity")]
[MemoryPackable]
public sealed partial class WitActivitySmtIntegrity : WitActivityFunction
{
    #region Functions

    /// <summary>The activity as a script names it.</summary>
    /// <returns>The references it is given.</returns>
    protected override string InnerString()
    {
        return string.Join(", ", new object?[] { Ledger }.OfType<object>());
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySmtIntegrity activity)
            return false;

        return base.Is(activity, tolerance)
               && Ledger.Check(activity.Ledger);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtIntegrity InnerClone()
    {
        return new WitActivitySmtIntegrity
        {
            Ledger = Ledger?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Ledger' argument of the activity: the settled run: the <c>Blob</c> of Smt.Settle.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Ledger { get; init; }

    #endregion
}
