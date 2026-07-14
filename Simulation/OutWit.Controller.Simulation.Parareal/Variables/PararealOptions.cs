using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Variables;

[Variable("PararealOptions")]
[MemoryPackable]
public sealed partial class WitVariablePararealOptions : WitVariable<PararealOptionsData?>, IWitVariableFactory<WitVariablePararealOptions>
{
    #region Constructors

    public WitVariablePararealOptions(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariablePararealOptions(string name, PararealOptionsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealOptions variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariablePararealOptions Clone()
    {
        return new WitVariablePararealOptions(Name, (PararealOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariablePararealOptions Create(string name)
    {
        return new WitVariablePararealOptions(name);
    }

    #endregion
}
