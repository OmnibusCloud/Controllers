using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Variables;

/// <summary>
/// Collection of parareal slab tasks. Output of
/// Parareal.MakeTasks/MakeSnapshotTasks, input to Grid.ForEach. An iteration
/// wave shrinks as the exact prefix grows (slabs behind the frontier need no
/// further work); the closing snapshot wave covers every slab again.
/// </summary>
[Variable("PararealTaskCollection")]
[MemoryPackable]
public sealed partial class WitVariablePararealTaskCollection : WitCollection<PararealTaskData?>, IWitVariableFactory<WitVariablePararealTaskCollection>
{
    #region Constructors

    /// <summary>
    /// Creates the collection with no items yet — the form a script declaration
    /// starts from before the first wave is planned.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariablePararealTaskCollection(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Deserialization constructor (MemoryPack): restores name and items
    /// together.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <param name="value">Deserialized items, one per slab of the wave.</param>
    [MemoryPackConstructor]
    public WitVariablePararealTaskCollection(string name, IReadOnlyList<PararealTaskData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealTaskCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariablePararealTaskCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (PararealTaskData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariablePararealTaskCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a collection of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty collection awaiting its first wave.</returns>
    public static WitVariablePararealTaskCollection Create(string name)
    {
        return new WitVariablePararealTaskCollection(name);
    }

    #endregion
}
