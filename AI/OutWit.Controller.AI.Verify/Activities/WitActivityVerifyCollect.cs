using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Host-side: gather the result batches into a single verdict report blob (aggregates +
/// one record per task).
/// </summary>
[Activity("Verify.Collect")]
[MemoryPackable]
public sealed partial class WitActivityVerifyCollect : WitActivityFunction
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
        if (modelBase is not WitActivityVerifyCollect activity)
            return false;

        return base.Is(activity, tolerance)
               && Results.Check(activity.Results)
               && Options.Check(activity.Options);
    }

    protected override WitActivityVerifyCollect InnerClone()
    {
        return new WitActivityVerifyCollect
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
