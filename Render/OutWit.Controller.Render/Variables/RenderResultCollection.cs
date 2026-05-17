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
/// Collection of render results (one per rendered frame or tile).
/// </summary>
[Variable("RenderResultCollection")]
[MemoryPackable]
public sealed partial class WitVariableRenderResultCollection : WitCollection<RenderResultData?>, IWitVariableFactory<WitVariableRenderResultCollection>
{
    #region Constructors

    public WitVariableRenderResultCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderResultCollection(string name, IReadOnlyList<RenderResultData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderResultCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableRenderResultCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (RenderResultData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableRenderResultCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderResultCollection Create(string name)
    {
        return new WitVariableRenderResultCollection(name);
    }

    #endregion
}
