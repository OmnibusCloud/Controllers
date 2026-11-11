using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: generator result packages + a checker spec blob → check task packages, one check per
/// candidate that passed with output, under the candidate's index. The check tasks are stored as
/// package blobs, like the tasks of Verify.Split; the second fan-out of VerifyGenerateAndCheck
/// starts from the references this returns.
/// </summary>
[Activity("Verify.MakeCheckTasks")]
[MemoryPackable]
public sealed partial class WitActivityVerifyMakeCheckTasks : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Candidates}, {Checker}, {Options}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyMakeCheckTasks activity)
            return false;

        return base.Is(activity, tolerance)
               && Candidates.Check(activity.Candidates)
               && Checker.Check(activity.Checker)
               && Options.Check(activity.Options);
    }

    protected override WitActivityVerifyMakeCheckTasks InnerClone()
    {
        return new WitActivityVerifyMakeCheckTasks
        {
            Candidates = Candidates?.Clone() as IWitReference,
            Checker = Checker?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Candidates' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Candidates { get; init; }

    /// <summary>Reference to the 'Checker' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Checker { get; init; }

    /// <summary>Reference to the 'Options' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
