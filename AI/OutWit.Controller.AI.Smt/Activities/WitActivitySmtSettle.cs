using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// Host-side: everything a run's fan-outs gave out and returned, with the problem set it came
/// from, becomes the settled run as one ledger blob. Every result package is taken or refused
/// here, under its ticket; every problem is ruled on here, and every <c>sat</c> answer is checked
/// here by the host's evaluator; the documents (results, report, provenance, integrity) are written
/// here, once. The last argument, when a script gives it, is the sampling rate its fan-outs ran
/// under: the repair round is then taken in as a sampled one, and the documents state the rate and
/// whether it was met.
/// </summary>
[Activity("Smt.Settle")]
[MemoryPackable]
public sealed partial class WitActivitySmtSettle : WitActivityFunction
{
    #region Functions

    /// <summary>The activity as a script names it.</summary>
    /// <returns>The references it is given.</returns>
    protected override string InnerString()
    {
        return string.Join(", ", new object?[] { Problems, Options, Packages, Results, Repairs, Repaired, Disputes, Settled, Rate }.OfType<object>());
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySmtSettle activity)
            return false;

        return base.Is(activity, tolerance)
               && Problems.Check(activity.Problems)
               && Options.Check(activity.Options)
               && Packages.Check(activity.Packages)
               && Results.Check(activity.Results)
               && Repairs.Check(activity.Repairs)
               && Repaired.Check(activity.Repaired)
               && Disputes.Check(activity.Disputes)
               && Settled.Check(activity.Settled)
               && Rate.Check(activity.Rate);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtSettle InnerClone()
    {
        return new WitActivitySmtSettle
        {
            Problems = Problems?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference,
            Packages = Packages?.Clone() as IWitReference,
            Results = Results?.Clone() as IWitReference,
            Repairs = Repairs?.Clone() as IWitReference,
            Repaired = Repaired?.Clone() as IWitReference,
            Disputes = Disputes?.Clone() as IWitReference,
            Settled = Settled?.Clone() as IWitReference,
            Rate = Rate?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Problems' argument of the activity: the problem set.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Problems { get; init; }

    /// <summary>Reference to the 'Options' argument of the activity: the run's options.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    /// <summary>Reference to the 'Packages' argument of the activity: the packages the first fan-out gave out.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Packages { get; init; }

    /// <summary>Reference to the 'Results' argument of the activity: what it returned.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Results { get; init; }

    /// <summary>Reference to the 'Repairs' argument of the activity: the packages of the repair round.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repairs { get; init; }

    /// <summary>Reference to the 'Repaired' argument of the activity: what it returned.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repaired { get; init; }

    /// <summary>Reference to the 'Disputes' argument of the activity: the packages of the dispute round (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Disputes { get; init; }

    /// <summary>Reference to the 'Settled' argument of the activity: what it returned (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Settled { get; init; }

    /// <summary>Reference to the 'Rate' argument of the activity: the sampling rate the fan-outs ran under (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Rate { get; init; }

    #endregion
}
