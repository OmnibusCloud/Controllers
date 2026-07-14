using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Variables;

[Variable("PararealTask")]
[MemoryPackable]
public sealed partial class WitVariablePararealTask : WitVariable<PararealTaskData?>, IWitVariableFactory<WitVariablePararealTask>
{
    #region Constructors

    public WitVariablePararealTask(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariablePararealTask(string name, PararealTaskData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealTask variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariablePararealTask Clone()
    {
        return new WitVariablePararealTask(Name, (PararealTaskData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariablePararealTask Create(string name)
    {
        return new WitVariablePararealTask(name);
    }

    #endregion
}
