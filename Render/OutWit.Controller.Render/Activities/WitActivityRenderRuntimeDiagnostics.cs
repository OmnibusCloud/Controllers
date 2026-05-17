using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Returns packaged render-runtime diagnostics for the current node.
/// </summary>
[Activity("Render.RuntimeDiagnostics")]
[CanRunInParallelOnClient(true)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[MemoryPackable]
public sealed partial class WitActivityRenderRuntimeDiagnostics : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return "Render.RuntimeDiagnostics()";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderRuntimeDiagnostics && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderRuntimeDiagnostics InnerClone()
    {
        return new WitActivityRenderRuntimeDiagnostics();
    }

    #endregion
}
