using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: everything Corpus.Split would check, reported instead of thrown — shard count,
/// refused origins, oversized sources, the traffic estimate, the server storage estimate
/// against the deployment's headroom, every manifest problem.
/// </summary>
[Activity("Corpus.Preflight")]
[MemoryPackable]
public sealed partial class WitActivityCorpusPreflight : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Manifest}, {LanguageModel}, {Options}";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityCorpusPreflight activity)
            return false;

        return base.Is(activity, tolerance)
               && Manifest.Check(activity.Manifest)
               && LanguageModel.Check(activity.LanguageModel)
               && Options.Check(activity.Options);
    }

    protected override WitActivityCorpusPreflight InnerClone()
    {
        return new WitActivityCorpusPreflight
        {
            Manifest = Manifest?.Clone() as IWitReference,
            LanguageModel = LanguageModel?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    /// <summary>Reference to the 'Manifest' blob argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Manifest { get; init; }

    /// <summary>Reference to the 'LanguageModel' blob argument (may be unset when the language stage is off).</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? LanguageModel { get; init; }

    /// <summary>Reference to the 'Options' argument.</summary>
    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
