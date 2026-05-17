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
/// Collection of render tasks. Output of Render.Split, input to Grid.ForEach.
/// </summary>
[Variable("RenderTaskCollection")]
[MemoryPackable]
public sealed partial class WitVariableRenderTaskCollection : WitCollection<RenderTaskData?>, IWitVariableFactory<WitVariableRenderTaskCollection>
{
    #region Constructors

    public WitVariableRenderTaskCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderTaskCollection(string name, IReadOnlyList<RenderTaskData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderTaskCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableRenderTaskCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (RenderTaskData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableRenderTaskCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderTaskCollection Create(string name)
    {
        return new WitVariableRenderTaskCollection(name);
    }

    #endregion
}
