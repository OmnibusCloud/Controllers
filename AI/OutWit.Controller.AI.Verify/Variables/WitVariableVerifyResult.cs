using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

[Variable("VerifyResult")]
[MemoryPackable]
public sealed partial class WitVariableVerifyResult : WitVariable<VerifyResultData?>, IWitVariableFactory<WitVariableVerifyResult>
{
    #region Constructors

    public WitVariableVerifyResult(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyResult(string name, VerifyResultData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyResult variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableVerifyResult Clone()
    {
        return new WitVariableVerifyResult(Name, (VerifyResultData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVerifyResult Create(string name)
    {
        return new WitVariableVerifyResult(name);
    }

    #endregion
}
