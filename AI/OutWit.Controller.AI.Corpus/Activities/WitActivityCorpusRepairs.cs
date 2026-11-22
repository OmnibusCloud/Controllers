using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: the shards the first round left without a result — every machine that held one
/// returned it as a fault of its own, or returned something the server dropped — as task packages
/// for one more round, each shard and each package under a new ticket. Empty when every shard has
/// a result or a dispute.
/// </summary>
[Activity("Corpus.Repairs")]
[MemoryPackable]
public sealed partial class WitActivityCorpusRepairs : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Tasks}, {Executions}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityCorpusRepairs activity)
            return false;

        return base.Is(activity, tolerance)
               && Tasks.Check(activity.Tasks)
               && Executions.Check(activity.Executions);
    }

    protected override WitActivityCorpusRepairs InnerClone()
    {
        return new WitActivityCorpusRepairs
        {
            Tasks = Tasks?.Clone() as IWitReference,
            Executions = Executions?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the job's tasks (the output of Corpus.Split): the server's own list of shards and first-round tickets.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Tasks { get; init; }

    /// <summary>Reference to what the first round returned.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Executions { get; init; }

    #endregion
}
