using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.CalculiX.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.CalculiX.Variables;

/// <summary>
/// Collection of variant solve results — the wave a chunk's Grid.ForEach
/// returns, in completion order (never source order: map by VariantIndex).
/// </summary>
[Variable("CcxResultCollection")]
[MemoryPackable]
public sealed partial class WitVariableCcxResultCollection : WitCollection<CcxResultData?>, IWitVariableFactory<WitVariableCcxResultCollection>
{
    #region Constructors

    /// <summary>
    /// Creates the collection with no items yet — the form used when the
    /// script declares it ahead of the first chunk.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableCcxResultCollection(string name)
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
    public WitVariableCcxResultCollection(string name, IReadOnlyList<CcxResultData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableCcxResultCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableCcxResultCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => x?.Clone())
            .ToArray() ?? [];

        return new WitVariableCcxResultCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a collection of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty collection awaiting its first chunk.</returns>
    public static WitVariableCcxResultCollection Create(string name)
    {
        return new WitVariableCcxResultCollection(name);
    }

    #endregion
}
