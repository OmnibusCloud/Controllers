using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Variables;

[Variable("PararealPlan")]
[MemoryPackable]
public sealed partial class WitVariablePararealPlan : WitVariable<PararealPlanData?>, IWitVariableFactory<WitVariablePararealPlan>
{
    #region Constructors

    public WitVariablePararealPlan(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariablePararealPlan(string name, PararealPlanData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealPlan variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariablePararealPlan Clone()
    {
        return new WitVariablePararealPlan(Name, (PararealPlanData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariablePararealPlan Create(string name)
    {
        return new WitVariablePararealPlan(name);
    }

    #endregion
}
