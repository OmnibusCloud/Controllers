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
/// Script variable carrying <see cref="PararealStateData"/>, the server-side
/// state of the iteration: opened by Parareal.Init, replaced each iteration by
/// Parareal.Correct, and read by Parareal.MakeTasks,
/// Parareal.MakeSnapshotTasks, Parareal.IsConverged and Parareal.Collect. It
/// holds slab-boundary blob ids and norms — never the fields themselves.
/// </summary>
[Variable("PararealState")]
[MemoryPackable]
public sealed partial class WitVariablePararealState : WitVariable<PararealStateData?>, IWitVariableFactory<WitVariablePararealState>
{
    #region Constructors

    /// <summary>
    /// Creates the variable without a value — the form a script declaration
    /// starts from before an activity assigns it.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariablePararealState(string name)
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
    public WitVariablePararealState(string name, PararealStateData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealState variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariablePararealState Clone()
    {
        return new WitVariablePararealState(Name, (PararealStateData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariablePararealState Create(string name)
    {
        return new WitVariablePararealState(name);
    }

    #endregion
}
