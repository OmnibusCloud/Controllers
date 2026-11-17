using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: the task packages a fan-out gave out and the result packages it returned → the
/// tasks that have no verdict yet, as new task packages to send out once more: the tasks of a
/// package no accepted result answers, and the tasks a node reported as a fault of its own. An
/// empty collection when every task has a verdict.
/// </summary>
[Activity("Verify.Repairs")]
[MemoryPackable]
public sealed partial class WitActivityVerifyRepairs : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Packages}, {Results}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyRepairs activity)
            return false;

        return base.Is(activity, tolerance)
               && Packages.Check(activity.Packages)
               && Results.Check(activity.Results);
    }

    protected override WitActivityVerifyRepairs InnerClone()
    {
        return new WitActivityVerifyRepairs
        {
            Packages = Packages?.Clone() as IWitReference,
            Results = Results?.Clone() as IWitReference
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

    #endregion
}
