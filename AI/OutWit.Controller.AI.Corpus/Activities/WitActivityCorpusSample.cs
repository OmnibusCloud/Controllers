using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: the sampled share of the tasks (CorpusOptions.SamplingRate) as replica tasks for
/// a second fan-out, in reverse order so the allocator lands most of them away from their
/// primaries.
/// </summary>
[Activity("Corpus.Sample")]
[MemoryPackable]
public sealed partial class WitActivityCorpusSample : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Tasks}, {Options}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityCorpusSample activity)
            return false;

        return base.Is(activity, tolerance)
               && Tasks.Check(activity.Tasks)
               && Options.Check(activity.Options);
    }

    protected override WitActivityCorpusSample InnerClone()
    {
        return new WitActivityCorpusSample
        {
            Tasks = Tasks?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Tasks' collection argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Tasks { get; init; }

    /// <summary>Reference to the 'Options' argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
