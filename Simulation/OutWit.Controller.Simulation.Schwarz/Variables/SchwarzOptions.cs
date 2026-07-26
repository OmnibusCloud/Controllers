using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

/// <summary>
/// Script variable carrying the user's SchwarzOptionsData tuning set — a job
/// input alongside the model blob; read by Schwarz.Decompose and
/// Schwarz.RoundBudget.
/// </summary>
[Variable("SchwarzOptions")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzOptions : WitVariable<SchwarzOptionsData?>, IWitVariableFactory<WitVariableSchwarzOptions>
{
    #region Constructors

    /// <summary>
    /// Creates the variable with no payload yet — the form used when the
    /// script declares it ahead of first assignment.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableSchwarzOptions(string name)
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

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariableSchwarzOptions Create(string name)
    {
        return new WitVariableSchwarzOptions(name);
    }

    #endregion
}
