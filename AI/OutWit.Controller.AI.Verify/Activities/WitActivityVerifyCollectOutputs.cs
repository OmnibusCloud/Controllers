using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: result batches → the corpus of outputs (one JSON line per task: verdict, exit,
/// stdout, stderr) as a blob. The product of VerifyGenerate; VerifyGenerateAndCheck keeps it
/// next to the check report so that skipped candidates stay visible.
/// </summary>
[Activity("Verify.CollectOutputs")]
[MemoryPackable]
public sealed partial class WitActivityVerifyCollectOutputs : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Results}, {Options}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityVerifyCollectOutputs activity)
            return false;

        return base.Is(activity, tolerance)
               && Results.Check(activity.Results)
               && Options.Check(activity.Options);
    }

    protected override WitActivityVerifyCollectOutputs InnerClone()
    {
        return new WitActivityVerifyCollectOutputs
        {
            Results = Results?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Results { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
