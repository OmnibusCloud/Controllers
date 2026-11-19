using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: the task packages a fan-out gave out and the result packages it returned → the
/// tasks whose executions disagree, as new task packages for a fan-out in three copies
/// (Grid.Replicas(3)). An empty collection when no executions disagree. With the repair round
/// as well (what it gave out and what it returned), a task the first fan-out left without a
/// verdict is taken from the repair round: when its executions there disagree it goes to the
/// dispute too.
/// </summary>
[Activity("Verify.Disputes")]
[MemoryPackable]
public sealed partial class WitActivityVerifyDisputes : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return Repairs == null ? $"{Packages}, {Results}" : $"{Packages}, {Results}, {Repairs}, {Repaired}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyDisputes activity)
            return false;

        return base.Is(activity, tolerance)
               && Packages.Check(activity.Packages)
               && Results.Check(activity.Results)
               && Repairs.Check(activity.Repairs)
               && Repaired.Check(activity.Repaired);
    }

    protected override WitActivityVerifyDisputes InnerClone()
    {
        return new WitActivityVerifyDisputes
        {
            Packages = Packages?.Clone() as IWitReference,
            Results = Results?.Clone() as IWitReference,
            Repairs = Repairs?.Clone() as IWitReference,
            Repaired = Repaired?.Clone() as IWitReference
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

    /// <summary>Reference to the 'Repairs' argument of the activity (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repairs { get; init; }

    /// <summary>Reference to the 'Repaired' argument of the activity (may be absent).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repaired { get; init; }

    #endregion
}
