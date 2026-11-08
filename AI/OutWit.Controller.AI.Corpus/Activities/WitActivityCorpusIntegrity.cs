using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: the sampled shards' primary and replica results compared on everything an
/// honest node cannot vary, as the integrity blob of the run.
/// </summary>
[Activity("Corpus.Integrity")]
[MemoryPackable]
public sealed partial class WitActivityCorpusIntegrity : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Results}, {Replicas}";
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
               && Results.Check(activity.Results)
               && Replicas.Check(activity.Replicas);
    }

    protected override WitActivityCorpusIntegrity InnerClone()
    {
        return new WitActivityCorpusIntegrity
        {
            Results = Results?.Clone() as IWitReference,
            Replicas = Replicas?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the primary 'Results' collection.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Results { get; init; }

    /// <summary>Reference to the 'Replicas' result collection.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Replicas { get; init; }

    #endregion
}
