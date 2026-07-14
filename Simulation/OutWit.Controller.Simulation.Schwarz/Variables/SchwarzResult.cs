using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

[Variable("SchwarzResult")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzResult : WitVariable<SchwarzResultData?>, IWitVariableFactory<WitVariableSchwarzResult>
{
    #region Constructors

    public WitVariableSchwarzResult(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableSchwarzResult(string name, SchwarzResultData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSchwarzResult variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableSchwarzResult Clone()
    {
        return new WitVariableSchwarzResult(Name, (SchwarzResultData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableSchwarzResult Create(string name)
    {
        return new WitVariableSchwarzResult(name);
    }

    #endregion
}
