using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Variables;

/// <summary>
/// Script variable carrying one slab's <see cref="PararealResultData"/> — what a
/// compute node returns from Parareal.Propagate. Scripts rarely name it on its
/// own: Grid.ForEach gathers a whole wave into a PararealResultCollection, which
/// is what Parareal.Correct and Parareal.Collect consume.
/// </summary>
[Variable("PararealResult")]
[MemoryPackable]
public sealed partial class WitVariablePararealResult : WitVariable<PararealResultData?>, IWitVariableFactory<WitVariablePararealResult>
{
    #region Constructors

    /// <summary>
    /// Creates the variable without a value — the form a script declaration
    /// starts from before an activity assigns it.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariablePararealResult(string name)
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
    public WitVariablePararealResult(string name, PararealResultData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealResult variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariablePararealResult Clone()
    {
        return new WitVariablePararealResult(Name, (PararealResultData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariablePararealResult Create(string name)
    {
        return new WitVariablePararealResult(name);
    }

    #endregion
}
