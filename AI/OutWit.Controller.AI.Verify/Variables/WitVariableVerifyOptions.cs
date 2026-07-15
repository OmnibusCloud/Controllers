using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

[Variable("VerifyOptions")]
[MemoryPackable]
public sealed partial class WitVariableVerifyOptions : WitVariable<VerifyOptionsData?>, IWitVariableFactory<WitVariableVerifyOptions>
{
    #region Constructors

    public WitVariableVerifyOptions(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyOptions(string name, VerifyOptionsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyOptions variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableVerifyOptions Clone()
    {
        return new WitVariableVerifyOptions(Name, (VerifyOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVerifyOptions Create(string name)
    {
        return new WitVariableVerifyOptions(name);
    }

    #endregion
}
