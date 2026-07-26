using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Math.Simulation;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

/// <summary>
/// Collection of Schwarz wave results — arrives in completion order; consumers re-key by SubdomainIndex.
/// </summary>
[Variable("SchwarzResultCollection")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzResultCollection : WitCollection<SchwarzResultData?>, IWitVariableFactory<WitVariableSchwarzResultCollection>
{
    #region Constructors

    /// <summary>
    /// Creates the collection with no items yet — the form used when the script
    /// declares it ahead of the first wave.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableSchwarzResultCollection(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Deserialization constructor: rehydrates name and items together when the
    /// collection crosses the wire.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <param name="value">Deserialized items, in the order they were gathered.</param>
    [MemoryPackConstructor]
    public WitVariableSchwarzResultCollection(string name, IReadOnlyList<SchwarzResultData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSchwarzResultCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableSchwarzResultCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (SchwarzResultData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableSchwarzResultCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a collection of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty collection awaiting its first wave.</returns>
    public static WitVariableSchwarzResultCollection Create(string name)
    {
        return new WitVariableSchwarzResultCollection(name);
    }

    #endregion
}
