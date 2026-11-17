using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: the integrity document of a run (a JSON blob) — the pairs of the first round
/// compared, the disputes and how each ended, the shards without a result, the values dropped,
/// by reason.
/// </summary>
[Activity("Corpus.Integrity")]
[MemoryPackable]
public sealed partial class WitActivityCorpusIntegrity : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Tasks}, {Executions}, {Repairs}, {Repaired}, {Disputes}, {Resolved}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityCorpusIntegrity activity)
            return false;

        return base.Is(activity, tolerance)
               && Tasks.Check(activity.Tasks)
               && Executions.Check(activity.Executions)
               && Repairs.Check(activity.Repairs)
               && Repaired.Check(activity.Repaired)
               && Disputes.Check(activity.Disputes)
               && Resolved.Check(activity.Resolved);
    }

    protected override WitActivityCorpusIntegrity InnerClone()
    {
        return new WitActivityCorpusIntegrity
        {
            Tasks = Tasks?.Clone() as IWitReference,
            Executions = Executions?.Clone() as IWitReference,
            Repairs = Repairs?.Clone() as IWitReference,
            Repaired = Repaired?.Clone() as IWitReference,
            Disputes = Disputes?.Clone() as IWitReference,
            Resolved = Resolved?.Clone() as IWitReference
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

    /// <summary>Reference to the dispute round's tasks (the output of Corpus.Disputes).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Disputes { get; init; }

    /// <summary>Reference to what the dispute round returned.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Resolved { get; init; }

    #endregion
}
