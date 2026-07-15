using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

[Variable("VerifyPreflight")]
[MemoryPackable]
public sealed partial class WitVariableVerifyPreflight : WitVariable<VerifyPreflightData?>, IWitVariableFactory<WitVariableVerifyPreflight>
{
    #region Constructors

    public WitVariableVerifyPreflight(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyPreflight(string name, VerifyPreflightData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyPreflight variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableVerifyPreflight Clone()
    {
        return new WitVariableVerifyPreflight(Name, (VerifyPreflightData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVerifyPreflight Create(string name)
    {
        return new WitVariableVerifyPreflight(name);
    }

    #endregion
}
