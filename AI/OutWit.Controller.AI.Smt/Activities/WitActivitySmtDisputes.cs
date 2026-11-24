using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// Host-side: the problems that go to three more machines are stored as packages: those whose
/// copies' digests differ, whose package came back more often than it was asked for, and whose
/// answer's model the host's evaluator calls failed (a failed model accuses nobody: the machines
/// settle it). A script that hands over its repair round asked two machines for each of its
/// packages; a problem the repair round answered is looked at with every answer it has, the lone
/// answer of the first fan-out among them, so an answer that differs from the repair round's is a
/// dispute too.
/// </summary>
[Activity("Smt.Disputes")]
[MemoryPackable]
public sealed partial class WitActivitySmtDisputes : WitActivityFunction
{
    #region Functions

    /// <summary>The activity as a script names it.</summary>
    /// <returns>The references it is given.</returns>
    protected override string InnerString()
    {
        return string.Join(", ", new object?[] { Packages, Results, Repairs, Repaired }.OfType<object>());
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySmtDisputes activity)
            return false;

        return base.Is(activity, tolerance)
               && Packages.Check(activity.Packages)
               && Results.Check(activity.Results)
               && Repairs.Check(activity.Repairs)
               && Repaired.Check(activity.Repaired);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtDisputes InnerClone()
    {
        return new WitActivitySmtDisputes
        {
            Packages = Packages?.Clone() as IWitReference,
            Results = Results?.Clone() as IWitReference,
            Repairs = Repairs?.Clone() as IWitReference,
            Repaired = Repaired?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Packages' argument of the activity: the packages the first fan-out gave out.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Packages { get; init; }

    /// <summary>Reference to the 'Results' argument of the activity: what it returned.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Results { get; init; }

    /// <summary>Reference to the 'Repairs' argument of the activity: the packages of the repair round (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repairs { get; init; }

    /// <summary>Reference to the 'Repaired' argument of the activity: what the repair round returned (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repaired { get; init; }

    #endregion
}
