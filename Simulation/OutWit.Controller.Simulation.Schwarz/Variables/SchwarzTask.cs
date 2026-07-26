using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

/// <summary>
/// Script variable carrying one subdomain's SchwarzTaskData — the work item
/// Grid.ForEach hands to Schwarz.SolveSubdomain on a compute node. Produced in
/// bulk by Schwarz.MakeTasks and Schwarz.MakeFinalTasks as a
/// SchwarzTaskCollection; the single-value form exists because the loop
/// variable of a Grid.ForEach needs a type of its own.
/// </summary>
[Variable("SchwarzTask")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzTask : WitVariable<SchwarzTaskData?>, IWitVariableFactory<WitVariableSchwarzTask>
{
    #region Constructors

    /// <summary>
    /// Creates the variable with no payload yet — the form used when the
    /// script declares it ahead of first assignment.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableSchwarzTask(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Deserialization constructor: rehydrates name and payload together when
    /// the variable crosses the wire.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <param name="value">Deserialized payload; null when the variable is unset.</param>
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

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariableSchwarzTask Create(string name)
    {
        return new WitVariableSchwarzTask(name);
    }

    #endregion
}
