using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

[Variable("SchwarzOptions")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzOptions : WitVariable<SchwarzOptionsData?>, IWitVariableFactory<WitVariableSchwarzOptions>
{
    #region Constructors

    public WitVariableSchwarzOptions(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableSchwarzOptions(string name, SchwarzOptionsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSchwarzOptions variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableSchwarzOptions Clone()
    {
        return new WitVariableSchwarzOptions(Name, (SchwarzOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableSchwarzOptions Create(string name)
    {
        return new WitVariableSchwarzOptions(name);
    }

    #endregion
}
