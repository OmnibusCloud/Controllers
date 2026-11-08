using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// The primitive: a node fetches one shard from its public origin and runs it through the
/// pipeline. A pure function of the task — the product's hash is the same on any node — so
/// retries and reassignment re-run it safely. Several shards may run on one client at once:
/// each is one core's worth of streaming work.
/// </summary>
[Activity("Corpus.ProcessShard")]
[CanRunInParallelOnClient(true)]
[MemoryPackable]
public sealed partial class WitActivityCorpusProcessShard : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Task}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityCorpusProcessShard activity)
            return false;

        return base.Is(activity, tolerance)
               && Task.Check(activity.Task);
    }

    protected override WitActivityCorpusProcessShard InnerClone()
    {
        return new WitActivityCorpusProcessShard
        {
            Task = Task?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Task' argument of the activity.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Task { get; init; }

    #endregion
}
