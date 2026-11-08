using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: the shard results become the job report (a JSON blob) — verdicts, product blobs
/// and content hashes per shard, the aggregate counters over the fleet.
/// </summary>
[Activity("Corpus.Collect")]
[MemoryPackable]
public sealed partial class WitActivityCorpusCollect : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Results}, {Options}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityCorpusCollect activity)
            return false;

        return base.Is(activity, tolerance)
               && Results.Check(activity.Results)
               && Options.Check(activity.Options);
    }

    protected override WitActivityCorpusCollect InnerClone()
    {
        return new WitActivityCorpusCollect
        {
            Results = Results?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Results' collection argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Results { get; init; }

    /// <summary>Reference to the 'Options' argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
