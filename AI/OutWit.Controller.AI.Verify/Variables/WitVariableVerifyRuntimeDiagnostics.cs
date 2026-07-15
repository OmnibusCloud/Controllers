using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

[Variable("VerifyRuntimeDiagnostics")]
[MemoryPackable]
public sealed partial class WitVariableVerifyRuntimeDiagnostics : WitVariable<VerifyRuntimeDiagnosticsData?>, IWitVariableFactory<WitVariableVerifyRuntimeDiagnostics>
{
    #region Constructors

    public WitVariableVerifyRuntimeDiagnostics(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyRuntimeDiagnostics(string name, VerifyRuntimeDiagnosticsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyRuntimeDiagnostics variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableVerifyRuntimeDiagnostics Clone()
    {
        return new WitVariableVerifyRuntimeDiagnostics(Name, (VerifyRuntimeDiagnosticsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVerifyRuntimeDiagnostics Create(string name)
    {
        return new WitVariableVerifyRuntimeDiagnostics(name);
    }

    #endregion
}
