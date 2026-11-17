using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: the shards whose copies disagreed (or that came back more often than they were sent
/// out) as tasks for the dispute round, each under a new ticket; the script runs them on three
/// machines (<c>Grid.Replicas(3)</c>). Empty when no copies disagreed.
/// </summary>
[Activity("Corpus.Disputes")]
[MemoryPackable]
public sealed partial class WitActivityCorpusDisputes : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Tasks}, {Executions}, {Repairs}, {Repaired}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityCorpusDisputes activity)
            return false;

        return base.Is(activity, tolerance)
               && Tasks.Check(activity.Tasks)
               && Executions.Check(activity.Executions)
               && Repairs.Check(activity.Repairs)
               && Repaired.Check(activity.Repaired);
    }

    protected override WitActivityCorpusDisputes InnerClone()
    {
        return new WitActivityCorpusDisputes
        {
            Tasks = Tasks?.Clone() as IWitReference,
            Executions = Executions?.Clone() as IWitReference,
            Repairs = Repairs?.Clone() as IWitReference,
            Repaired = Repaired?.Clone() as IWitReference
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

    /// <summary>Reference to the repair round's tasks (the output of Corpus.Repairs).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repairs { get; init; }

    /// <summary>Reference to what the repair round returned.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Repaired { get; init; }

    #endregion
}
