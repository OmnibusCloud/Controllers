using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Collections;

/// <summary>
/// Collection of ParaView render batches. Output of ParaView.SplitBatched, input to Grid.ForEach over ParaView.RenderFrameBatch.
/// </summary>
[Variable("ParaViewRenderTaskBatchCollection")]
[MemoryPackable]
public sealed partial class WitVariableParaViewRenderTaskBatchCollection : WitCollection<ParaViewRenderTaskBatchData?>, IWitVariableFactory<WitVariableParaViewRenderTaskBatchCollection>
{
    #region Constructors

    /// <summary>
    /// Creates an empty variable.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    public WitVariableParaViewRenderTaskBatchCollection(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Creates a variable holding a value.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <param name=\"value\">Initial value.</param>
    [MemoryPackConstructor]
    public WitVariableParaViewRenderTaskBatchCollection(string name, IReadOnlyList<ParaViewRenderTaskBatchData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewRenderTaskBatchCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    /// <inheritdoc />
    public override WitVariableParaViewRenderTaskBatchCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(me => (ParaViewRenderTaskBatchData?)me?.Clone())
            .ToArray() ?? [];

        return new WitVariableParaViewRenderTaskBatchCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Creates an empty variable (factory contract of the engine).
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <returns>The variable.</returns>
    public static WitVariableParaViewRenderTaskBatchCollection Create(string name)
    {
        return new WitVariableParaViewRenderTaskBatchCollection(name);
    }

    #endregion
}
