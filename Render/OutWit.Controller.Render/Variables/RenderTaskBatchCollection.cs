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
/// Collection of render-task chunks. Output of Render.SplitBatched, input to Grid.ForEach
/// (each chunk is rendered in one Blender process by Render.FrameBatch).
/// </summary>
[Variable("RenderTaskBatchCollection")]
[MemoryPackable]
public sealed partial class WitVariableRenderTaskBatchCollection : WitCollection<RenderTaskBatchData?>, IWitVariableFactory<WitVariableRenderTaskBatchCollection>
{
    #region Constructors

    public WitVariableRenderTaskBatchCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderTaskBatchCollection(string name, IReadOnlyList<RenderTaskBatchData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderTaskBatchCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableRenderTaskBatchCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (RenderTaskBatchData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableRenderTaskBatchCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderTaskBatchCollection Create(string name)
    {
        return new WitVariableRenderTaskBatchCollection(name);
    }

    #endregion
}
