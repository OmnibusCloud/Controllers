using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Math.Simulation;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

/// <summary>
/// Script variable carrying the SchwarzPlanData decomposition — written once by
/// Schwarz.Decompose and then read unchanged by Schwarz.MakeTasks,
/// Schwarz.MakeFinalTasks and Schwarz.Assemble for the rest of the job.
/// </summary>
[Variable("SchwarzPlan")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzPlan : WitVariable<SchwarzPlanData?>, IWitVariableFactory<WitVariableSchwarzPlan>
{
    #region Constructors

    /// <summary>
    /// Creates the variable with no payload yet — the form used when the
    /// script declares it ahead of first assignment.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableSchwarzPlan(string name)
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

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariableSchwarzPlan Create(string name)
    {
        return new WitVariableSchwarzPlan(name);
    }

    #endregion
}
