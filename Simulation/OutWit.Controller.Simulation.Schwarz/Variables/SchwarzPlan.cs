using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

[Variable("SchwarzPlan")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzPlan : WitVariable<SchwarzPlanData?>, IWitVariableFactory<WitVariableSchwarzPlan>
{
    #region Constructors

    public WitVariableSchwarzPlan(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableSchwarzPlan(string name, SchwarzPlanData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSchwarzPlan variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableSchwarzPlan Clone()
    {
        return new WitVariableSchwarzPlan(Name, (SchwarzPlanData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableSchwarzPlan Create(string name)
    {
        return new WitVariableSchwarzPlan(name);
    }

    #endregion
}
