using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Collections;

/// <summary>
/// Collection of ParaView render results (one per rendered output). Output of Grid.ForEach over ParaView.RenderFrame, input to ParaView.Collect.
/// </summary>
[Variable("ParaViewRenderResultCollection")]
[MemoryPackable]
public sealed partial class WitVariableParaViewRenderResultCollection : WitCollection<ParaViewRenderResultData?>, IWitVariableFactory<WitVariableParaViewRenderResultCollection>
{
    #region Constructors

    /// <summary>
    /// Creates an empty variable.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    public WitVariableParaViewRenderResultCollection(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Creates a variable holding a value.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <param name=\"value\">Initial value.</param>
    [MemoryPackConstructor]
    public WitVariableParaViewRenderResultCollection(string name, IReadOnlyList<ParaViewRenderResultData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewRenderResultCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    /// <inheritdoc />
    public override WitVariableParaViewRenderResultCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(me => (ParaViewRenderResultData?)me?.Clone())
            .ToArray() ?? [];

        return new WitVariableParaViewRenderResultCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Creates an empty variable (factory contract of the engine).
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <returns>The variable.</returns>
    public static WitVariableParaViewRenderResultCollection Create(string name)
    {
        return new WitVariableParaViewRenderResultCollection(name);
    }

    #endregion
}
