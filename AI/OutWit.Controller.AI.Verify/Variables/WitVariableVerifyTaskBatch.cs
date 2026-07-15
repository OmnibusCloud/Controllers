using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

[Variable("VerifyTaskBatch")]
[MemoryPackable]
public sealed partial class WitVariableVerifyTaskBatch : WitVariable<VerifyTaskBatchData?>, IWitVariableFactory<WitVariableVerifyTaskBatch>
{
    #region Constructors

    public WitVariableVerifyTaskBatch(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyTaskBatch(string name, VerifyTaskBatchData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyTaskBatch variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableVerifyTaskBatch Clone()
    {
        return new WitVariableVerifyTaskBatch(Name, (VerifyTaskBatchData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVerifyTaskBatch Create(string name)
    {
        return new WitVariableVerifyTaskBatch(name);
    }

    #endregion
}
