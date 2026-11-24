using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Activities;

/// <summary>
/// Host-side: what the first fan-out returned is taken in under its tickets, and the problems
/// that go out once more are stored as packages: those without an answer the host takes (a
/// package that came back with a node's fault code, without an answer, refused, or whose rows are
/// all faults of the node), and those of a package the fan-out asked for twice that came back with
/// fewer than two answers. The last argument, when a script gives it, is the sampling rate the
/// fan-out ran under: a rate that asks for every package tells the host that every package was
/// asked for twice, even one of which a single result came back.
/// </summary>
[Activity("Smt.Repairs")]
[MemoryPackable]
public sealed partial class WitActivitySmtRepairs : WitActivityFunction
{
    #region Functions

    /// <summary>The activity as a script names it.</summary>
    /// <returns>The references it is given.</returns>
    protected override string InnerString()
    {
        return string.Join(", ", new object?[] { Packages, Results, Rate }.OfType<object>());
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySmtRepairs activity)
            return false;

        return base.Is(activity, tolerance)
               && Packages.Check(activity.Packages)
               && Results.Check(activity.Results)
               && Rate.Check(activity.Rate);
    }

    /// <summary>Deep copy of the activity's own part.</summary>
    /// <returns>The copy.</returns>
    protected override WitActivitySmtRepairs InnerClone()
    {
        return new WitActivitySmtRepairs
        {
            Packages = Packages?.Clone() as IWitReference,
            Results = Results?.Clone() as IWitReference,
            Rate = Rate?.Clone() as IWitReference
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

    /// <summary>Reference to the 'Rate' argument of the activity: the sampling rate the first fan-out ran under (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Rate { get; init; }

    #endregion
}
