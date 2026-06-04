using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Collection of per-chunk render results. Output of Grid.ForEach over Render.FrameBatch; input to
/// Render.Collect / Render.CollectTiles, which flatten it via SelectMany(batch =&gt; batch.Results).
/// </summary>
[Variable("RenderResultBatchCollection")]
[MemoryPackable]
public sealed partial class WitVariableRenderResultBatchCollection : WitCollection<RenderResultBatchData?>, IWitVariableFactory<WitVariableRenderResultBatchCollection>
{
    #region Constructors

    public WitVariableRenderResultBatchCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderResultBatchCollection(string name, IReadOnlyList<RenderResultBatchData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderResultBatchCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableRenderResultBatchCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (RenderResultBatchData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableRenderResultBatchCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderResultBatchCollection Create(string name)
    {
        return new WitVariableRenderResultBatchCollection(name);
    }

    #endregion
}
