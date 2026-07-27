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
/// Script variable carrying one slab's <see cref="PararealTaskData"/> — the work
/// item Grid.ForEach hands to Parareal.Propagate on a compute node. Produced in
/// bulk by Parareal.MakeTasks and Parareal.MakeSnapshotTasks as a
/// PararealTaskCollection; the single-value form exists because the loop
/// variable of a Grid.ForEach needs a type of its own.
/// </summary>
[Variable("PararealTask")]
[MemoryPackable]
public sealed partial class WitVariablePararealTask : WitVariable<PararealTaskData?>, IWitVariableFactory<WitVariablePararealTask>
{
    #region Constructors

    /// <summary>
    /// Creates the variable without a value — the form a script declaration
    /// starts from before an activity assigns it.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariablePararealTask(string name)
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

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariablePararealTask Create(string name)
    {
        return new WitVariablePararealTask(name);
    }

    #endregion
}
