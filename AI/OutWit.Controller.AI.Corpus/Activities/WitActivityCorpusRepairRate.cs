using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: the share of the repair round's packages that run on two machines, as the number
/// Grid.Replicas takes — every one of them (1) when the run asked for sampling at all
/// (CorpusOptions.SamplingRate above 0), none (0) when it asked for none. The server does not
/// learn which packages of the first round the platform picked for a second machine, so a round
/// that came back short keeps its checking only if every shard that goes out again goes out twice.
/// </summary>
[Activity("Corpus.RepairRate")]
[MemoryPackable]
public sealed partial class WitActivityCorpusRepairRate : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Options}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityCorpusRepairRate activity)
            return false;

        return base.Is(activity, tolerance)
               && Options.Check(activity.Options);
    }

    protected override WitActivityCorpusRepairRate InnerClone()
    {
        return new WitActivityCorpusRepairRate
        {
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Options' argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
