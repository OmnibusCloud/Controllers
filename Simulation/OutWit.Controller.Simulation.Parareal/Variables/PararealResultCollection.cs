using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Math.Simulation;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;
using OutWit.Math.Simulation.Model.Parareal;

namespace OutWit.Controller.Simulation.Parareal.Variables;

/// <summary>
/// Collection of parareal wave results — arrives in completion order, not slab
/// order; Parareal.Correct and Parareal.Collect re-key it by SlabIndex and
/// reject a wave that is not a permutation of the slabs they expected.
/// </summary>
[Variable("PararealResultCollection")]
[MemoryPackable]
public sealed partial class WitVariablePararealResultCollection : WitCollection<PararealResultData?>, IWitVariableFactory<WitVariablePararealResultCollection>
{
    #region Constructors

    /// <summary>
    /// Creates the collection with no items yet — the form a script declaration
    /// starts from before the first wave lands.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariablePararealResultCollection(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Deserialization constructor (MemoryPack): restores name and items
    /// together.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <param name="value">Deserialized items, in the order they were gathered.</param>
    [MemoryPackConstructor]
    public WitVariablePararealResultCollection(string name, IReadOnlyList<PararealResultData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealResultCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariablePararealResultCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (PararealResultData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariablePararealResultCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a collection of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty collection awaiting its first wave.</returns>
    public static WitVariablePararealResultCollection Create(string name)
    {
        return new WitVariablePararealResultCollection(name);
    }

    #endregion
}
