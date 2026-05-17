using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping packaged render runtime diagnostics.
/// </summary>
[Variable("RenderRuntimeDiagnostics")]
[MemoryPackable]
public sealed partial class WitVariableRenderRuntimeDiagnostics : WitVariable<RenderRuntimeDiagnosticsData?>, IWitVariableFactory<WitVariableRenderRuntimeDiagnostics>
{
    #region Constructors

    public WitVariableRenderRuntimeDiagnostics(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderRuntimeDiagnostics(string name, RenderRuntimeDiagnosticsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderRuntimeDiagnostics variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderRuntimeDiagnostics Clone()
    {
        return new WitVariableRenderRuntimeDiagnostics(Name, (RenderRuntimeDiagnosticsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderRuntimeDiagnostics Create(string name)
    {
        return new WitVariableRenderRuntimeDiagnostics(name);
    }

    #endregion
}
