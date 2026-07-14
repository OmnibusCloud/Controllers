using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

[Variable("SchwarzTask")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzTask : WitVariable<SchwarzTaskData?>, IWitVariableFactory<WitVariableSchwarzTask>
{
    #region Constructors

    public WitVariableSchwarzTask(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableSchwarzTask(string name, SchwarzTaskData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSchwarzTask variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableSchwarzTask Clone()
    {
        return new WitVariableSchwarzTask(Name, (SchwarzTaskData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableSchwarzTask Create(string name)
    {
        return new WitVariableSchwarzTask(name);
    }

    #endregion
}
