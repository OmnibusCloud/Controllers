using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// Host-side: the provenance of a settled run, as a JSON blob (<c>docs/smt-provenance.schema.json</c>):
/// the pins it ran with, the options as resolved and the problem set's SHA-256.
/// </summary>
[Activity("Smt.Provenance")]
[MemoryPackable]
public sealed partial class WitActivitySmtProvenance : WitActivityFunction
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
        if (modelBase is not WitActivitySmtProvenance activity)
            return false;

        return base.Is(activity, tolerance)
               && Ledger.Check(activity.Ledger);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtProvenance InnerClone()
    {
        return new WitActivitySmtProvenance
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
