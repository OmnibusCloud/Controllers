using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

[Variable("SchwarzRound")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzRound : WitVariable<SchwarzRoundData?>, IWitVariableFactory<WitVariableSchwarzRound>
{
    #region Constructors

    public WitVariableSchwarzRound(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableSchwarzRound(string name, SchwarzRoundData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSchwarzRound variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableSchwarzRound Clone()
    {
        return new WitVariableSchwarzRound(Name, (SchwarzRoundData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableSchwarzRound Create(string name)
    {
        return new WitVariableSchwarzRound(name);
    }

    #endregion
}
