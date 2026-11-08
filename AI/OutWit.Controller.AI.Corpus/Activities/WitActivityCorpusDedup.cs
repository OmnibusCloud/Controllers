using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: the near-duplicate merge over the signatures of a finished run, from its report
/// — per shard a keep mask, and the clusters — as the dedup blob.
/// </summary>
[Activity("Corpus.Dedup")]
[MemoryPackable]
public sealed partial class WitActivityCorpusDedup : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Report}, {Options}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityCorpusDedup activity)
            return false;

        return base.Is(activity, tolerance)
               && Report.Check(activity.Report)
               && Options.Check(activity.Options);
    }

    protected override WitActivityCorpusDedup InnerClone()
    {
        return new WitActivityCorpusDedup
        {
            Report = Report?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Report' blob argument (a corpus-report/1 document).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Report { get; init; }

    /// <summary>Reference to the 'Options' argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
