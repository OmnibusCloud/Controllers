using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: parse the manifest blob, check every source against the allowlist and the cap,
/// pin the language model, hold the server storage estimate to the deployment's headroom, and
/// emit one task per shard for Grid.ForEach.
/// </summary>
[Activity("Corpus.Split")]
[MemoryPackable]
public sealed partial class WitActivityCorpusSplit : WitActivityFunction
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
        if (modelBase is not WitActivityCorpusSplit activity)
            return false;

        return base.Is(activity, tolerance)
               && Manifest.Check(activity.Manifest)
               && LanguageModel.Check(activity.LanguageModel)
               && Options.Check(activity.Options);
    }

    protected override WitActivityCorpusSplit InnerClone()
    {
        return new WitActivityCorpusSplit
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
