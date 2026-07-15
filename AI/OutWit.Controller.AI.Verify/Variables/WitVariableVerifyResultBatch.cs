using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

[Variable("VerifyResultBatch")]
[MemoryPackable]
public sealed partial class WitVariableVerifyResultBatch : WitVariable<VerifyResultBatchData?>, IWitVariableFactory<WitVariableVerifyResultBatch>
{
    #region Constructors

    public WitVariableVerifyResultBatch(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyResultBatch(string name, VerifyResultBatchData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyResultBatch variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableVerifyResultBatch Clone()
    {
        return new WitVariableVerifyResultBatch(Name, (VerifyResultBatchData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVerifyResultBatch Create(string name)
    {
        return new WitVariableVerifyResultBatch(name);
    }

    #endregion
}
