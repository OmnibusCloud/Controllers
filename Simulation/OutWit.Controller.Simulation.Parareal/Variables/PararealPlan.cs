using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Math.Simulation;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using OutWit.Math.Simulation.Model.Parareal;

namespace OutWit.Controller.Simulation.Parareal.Variables;

/// <summary>
/// Script variable carrying <see cref="PararealPlanData"/> — the slab plan
/// Parareal.Slice produces once and every later stage reads; immutable for the
/// rest of the job.
/// </summary>
[Variable("PararealPlan")]
[MemoryPackable]
public sealed partial class WitVariablePararealPlan : WitVariable<PararealPlanData?>, IWitVariableFactory<WitVariablePararealPlan>
{
    #region Constructors

    /// <summary>
    /// Creates the variable without a value — the form a script declaration
    /// starts from before an activity assigns it.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariablePararealPlan(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Deserialization constructor (MemoryPack): restores the variable with its
    /// payload in place.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <param name="value">Carried payload, or null when unset.</param>
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

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariablePararealPlan Create(string name)
    {
        return new WitVariablePararealPlan(name);
    }

    #endregion
}
