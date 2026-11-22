using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.AI.Corpus.Activities;

/// <summary>
/// Reports what the controller module of the process that evaluates it carries: the version,
/// the stage set and its fingerprint computed there, the parser and model versions and hashes,
/// the origin rule, the caps. A plain script function runs on the host, so this is the
/// server's module; the nodes run the same version. Mirrors Verify.RuntimeDiagnostics.
/// </summary>
[Activity("Corpus.RuntimeDiagnostics")]
[MemoryPackable]
public sealed partial class WitActivityCorpusRuntimeDiagnostics : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return "Corpus.RuntimeDiagnostics";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityCorpusRuntimeDiagnostics activity && base.Is(activity, tolerance);
    }

    protected override WitActivityCorpusRuntimeDiagnostics InnerClone()
    {
        return new WitActivityCorpusRuntimeDiagnostics();
    }

    #endregion
}
