using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Variables;

[Variable("PararealState")]
[MemoryPackable]
public sealed partial class WitVariablePararealState : WitVariable<PararealStateData?>, IWitVariableFactory<WitVariablePararealState>
{
    #region Constructors

    public WitVariablePararealState(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariablePararealState(string name, PararealStateData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealState variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariablePararealState Clone()
    {
        return new WitVariablePararealState(Name, (PararealStateData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariablePararealState Create(string name)
    {
        return new WitVariablePararealState(name);
    }

    #endregion
}
