using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Math.Simulation;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Variables;

/// <summary>
/// Script variable carrying <see cref="PararealOptionsData"/> — the user-facing
/// tuning knobs a PararealSolve job starts from; consumed by Parareal.Slice and
/// Parareal.IterationBudget.
/// </summary>
[Variable("PararealOptions")]
[MemoryPackable]
public sealed partial class WitVariablePararealOptions : WitVariable<PararealOptionsData?>, IWitVariableFactory<WitVariablePararealOptions>
{
    #region Constructors

    /// <summary>
    /// Creates the variable without a value — the form a script declaration
    /// starts from before an activity assigns it.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariablePararealOptions(string name)
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
    public WitVariablePararealOptions(string name, PararealOptionsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealOptions variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariablePararealOptions Clone()
    {
        return new WitVariablePararealOptions(Name, (PararealOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariablePararealOptions Create(string name)
    {
        return new WitVariablePararealOptions(name);
    }

    #endregion
}
