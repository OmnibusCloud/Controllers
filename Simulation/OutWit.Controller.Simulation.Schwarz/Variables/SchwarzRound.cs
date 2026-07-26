using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Math.Simulation;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

/// <summary>
/// Script variable carrying SchwarzRoundData, the server-side state of the
/// iteration: opened by Schwarz.InitRound, replaced each round by
/// Schwarz.Advance, and read by Schwarz.MakeTasks, Schwarz.MakeFinalTasks,
/// Schwarz.IsConverged and Schwarz.Assemble. It holds only ids and norms —
/// the fields themselves stay in blobs.
/// </summary>
[Variable("SchwarzRound")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzRound : WitVariable<SchwarzRoundData?>, IWitVariableFactory<WitVariableSchwarzRound>
{
    #region Constructors

    /// <summary>
    /// Creates the variable with no payload yet — the form used when the
    /// script declares it ahead of first assignment.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableSchwarzRound(string name)
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

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariableSchwarzRound Create(string name)
    {
        return new WitVariableSchwarzRound(name);
    }

    #endregion
}
