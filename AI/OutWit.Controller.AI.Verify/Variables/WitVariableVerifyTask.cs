using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Variables;

[Variable("VerifyTask")]
[MemoryPackable]
public sealed partial class WitVariableVerifyTask : WitVariable<VerifyTaskData?>, IWitVariableFactory<WitVariableVerifyTask>
{
    #region Constructors

    public WitVariableVerifyTask(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVerifyTask(string name, VerifyTaskData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableVerifyTask variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableVerifyTask Clone()
    {
        return new WitVariableVerifyTask(Name, (VerifyTaskData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVerifyTask Create(string name)
    {
        return new WitVariableVerifyTask(name);
    }

    #endregion
}
