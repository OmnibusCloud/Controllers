using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Sweep.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Variables;

/// <summary>
/// Script variable carrying the chunk schedule of a run, set once by Sweep.InitState; a monitoring client reads it beside the state.
/// </summary>
[Variable("SweepSchedule")]
[MemoryPackable]
public sealed partial class WitVariableSweepSchedule : WitVariable<SweepScheduleData?>, IWitVariableFactory<WitVariableSweepSchedule>
{
    #region Constructors

    /// <summary>
    /// Creates the variable with no payload yet — the form used when the
    /// script declares it ahead of first assignment.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableSweepSchedule(string name)
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
    public WitVariableSweepSchedule(string name, SweepScheduleData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSweepSchedule variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableSweepSchedule Clone()
    {
        return new WitVariableSweepSchedule(Name, GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariableSweepSchedule Create(string name)
    {
        return new WitVariableSweepSchedule(name);
    }

    #endregion
}
