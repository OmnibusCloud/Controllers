using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Variables;

[Variable("PararealResult")]
[MemoryPackable]
public sealed partial class WitVariablePararealResult : WitVariable<PararealResultData?>, IWitVariableFactory<WitVariablePararealResult>
{
    #region Constructors

    public WitVariablePararealResult(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariablePararealResult(string name, PararealResultData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealResult variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariablePararealResult Clone()
    {
        return new WitVariablePararealResult(Name, (PararealResultData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariablePararealResult Create(string name)
    {
        return new WitVariablePararealResult(name);
    }

    #endregion
}
