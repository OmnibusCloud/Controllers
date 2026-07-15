using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.AI.Verify.Activities;

/// <summary>
/// Node-side: report the node's sandbox runtime inventory (wasmtime version + pinned
/// language runtimes with hashes). Mirrors Render.RuntimeDiagnostics.
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
