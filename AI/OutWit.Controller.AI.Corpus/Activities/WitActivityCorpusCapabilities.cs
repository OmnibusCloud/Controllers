using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Host-side: what an application can know before a run, as a JSON document blob — the
/// controller's version and stage set, the parsers and the language-model format, where a
/// machine fetches from, the defaults and ceilings, the stage parameters a job may set, the
/// products, the storage ratios with the deployment's headroom, and the pool as totals (no
/// node is named). Takes no manifest, so an application can ask once and show it next to its
/// own preflight.
/// </summary>
[Activity("Corpus.Capabilities")]
[MemoryPackable]
public sealed partial class WitActivityCorpusCapabilities : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return "Corpus.Capabilities";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityCorpusCapabilities activity && base.Is(activity, tolerance);
    }

    protected override WitActivityCorpusCapabilities InnerClone()
    {
        return new WitActivityCorpusCapabilities();
    }

    #endregion
}
