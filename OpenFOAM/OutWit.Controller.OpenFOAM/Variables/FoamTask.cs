using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Variables;

/// <summary>
/// Script variable carrying one variant's FoamTaskData - the work item
/// Grid.ForEach hands to Foam.Run on a compute node. Produced in bulk as a
/// FoamTaskCollection; the single-value form exists because the loop variable
/// of a Grid.ForEach needs a type of its own.
/// </summary>
[Variable("FoamTask")]
[MemoryPackable]
public sealed partial class WitVariableFoamTask : WitVariable<FoamTaskData?>, IWitVariableFactory<WitVariableFoamTask>
{
    #region Constructors

    /// <summary>
    /// Creates the variable with no payload yet - the form used when the
    /// script declares it ahead of first assignment.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableFoamTask(string name)
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
    public WitVariableFoamTask(string name, FoamTaskData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableFoamTask variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableFoamTask Clone()
    {
        return new WitVariableFoamTask(Name, GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariableFoamTask Create(string name)
    {
        return new WitVariableFoamTask(name);
    }

    #endregion
}
