using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: everything a run's fan-outs gave out and returned (the first fan-out, the repair
/// round, and the dispute round when the script ran one) → the settled run as one ledger blob.
/// Every result package is accepted or refused here, under its ticket; every task is ruled on
/// here; the four documents (report, outputs, provenance, integrity) are written here, once.
/// </summary>
[Activity("Verify.Settle")]
[MemoryPackable]
public sealed partial class WitActivityVerifySettle : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Packages}, {Results}, {Repairs}, {Repaired}, {Disputes}, {Settled}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifySettle activity)
            return false;

        return base.Is(activity, tolerance)
               && Packages.Check(activity.Packages)
               && Results.Check(activity.Results)
               && Repairs.Check(activity.Repairs)
               && Repaired.Check(activity.Repaired)
               && Disputes.Check(activity.Disputes)
               && Settled.Check(activity.Settled);
    }

    protected override WitActivityVerifySettle InnerClone()
    {
        return new WitActivityVerifySettle
        {
            Packages = Packages?.Clone() as IWitReference,
            Results = Results?.Clone() as IWitReference,
            Repairs = Repairs?.Clone() as IWitReference,
            Repaired = Repaired?.Clone() as IWitReference,
            Disputes = Disputes?.Clone() as IWitReference,
            Settled = Settled?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Packages' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Packages { get; init; }

    /// <summary>Reference to the 'Results' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Results { get; init; }

    /// <summary>Reference to the 'Repairs' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repairs { get; init; }

    /// <summary>Reference to the 'Repaired' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repaired { get; init; }

    /// <summary>Reference to the 'Disputes' argument of the activity (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Disputes { get; init; }

    /// <summary>Reference to the 'Settled' argument of the activity (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Settled { get; init; }

    #endregion
}
