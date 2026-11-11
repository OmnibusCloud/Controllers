using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Reports the sandbox runtime inventory (wasmtime version + pinned language runtimes with
/// hashes) of the process that evaluates it. A plain script function runs on the host, so
/// this is the server's module; the fleet's modules are exercised by <c>Verify.ExecutePackage</c>
/// inside <c>Grid.ForEach</c>, where each package lands on a node. Mirrors Render.RuntimeDiagnostics.
/// </summary>
[Activity("Verify.RuntimeDiagnostics")]
[MemoryPackable]
public sealed partial class WitActivityVerifyRuntimeDiagnostics : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return "Verify.RuntimeDiagnostics";
    }

    #endregion

    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityVerifyRuntimeDiagnostics activity && base.Is(activity, tolerance);
    }

    protected override WitActivityVerifyRuntimeDiagnostics InnerClone()
    {
        return new WitActivityVerifyRuntimeDiagnostics();
    }

    #endregion
}
