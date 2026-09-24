using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Variables;

/// <summary>
/// Collection of variant run tasks - one chunk of a sweep, the input of a
/// Grid.ForEach over Foam.Run.
/// </summary>
[Variable("FoamTaskCollection")]
[MemoryPackable]
public sealed partial class WitVariableFoamTaskCollection : WitCollection<FoamTaskData?>, IWitVariableFactory<WitVariableFoamTaskCollection>
{
    #region Constructors

    /// <summary>
    /// Creates the collection with no items yet - the form used when the
    /// script declares it ahead of the first chunk.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableFoamTaskCollection(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Deserialization constructor: rehydrates name and items together when
    /// the collection crosses the wire.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <param name="value">Deserialized items, one per variant of the chunk.</param>
    [MemoryPackConstructor]
    public WitVariableFoamTaskCollection(string name, IReadOnlyList<FoamTaskData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableFoamTaskCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableFoamTaskCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => x?.Clone())
            .ToArray() ?? [];

        return new WitVariableFoamTaskCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a collection of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty collection awaiting its first chunk.</returns>
    public static WitVariableFoamTaskCollection Create(string name)
    {
        return new WitVariableFoamTaskCollection(name);
    }

    #endregion
}
