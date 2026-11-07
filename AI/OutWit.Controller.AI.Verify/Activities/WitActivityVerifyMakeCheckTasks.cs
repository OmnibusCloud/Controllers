using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: generator result batches + a checker spec blob → check task batches, one check per
/// candidate that passed with output, under the candidate's index. The second fan-out of
/// VerifyGenerateAndCheck starts from its return value.
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

    [MemoryPackAllowSerialize]
    public IWitReference? Candidates { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Checker { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
