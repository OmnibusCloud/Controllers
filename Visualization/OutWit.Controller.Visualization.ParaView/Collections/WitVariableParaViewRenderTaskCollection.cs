using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Collections;

/// <summary>
/// Collection of ParaView render tasks. Output of ParaView.Split, input to Grid.ForEach.
/// </summary>
[Variable("ParaViewRenderTaskCollection")]
[MemoryPackable]
public sealed partial class WitVariableParaViewRenderTaskCollection : WitCollection<ParaViewRenderTaskData?>, IWitVariableFactory<WitVariableParaViewRenderTaskCollection>
{
    #region Constructors

    /// <summary>
    /// Creates an empty variable.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    public WitVariableParaViewRenderTaskCollection(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Creates a variable holding a value.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <param name=\"value\">Initial value.</param>
    [MemoryPackConstructor]
    public WitVariableParaViewRenderTaskCollection(string name, IReadOnlyList<ParaViewRenderTaskData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewRenderTaskCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    /// <inheritdoc />
    public override WitVariableParaViewRenderTaskCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(me => (ParaViewRenderTaskData?)me?.Clone())
            .ToArray() ?? [];

        return new WitVariableParaViewRenderTaskCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Creates an empty variable (factory contract of the engine).
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <returns>The variable.</returns>
    public static WitVariableParaViewRenderTaskCollection Create(string name)
    {
        return new WitVariableParaViewRenderTaskCollection(name);
    }

    #endregion
}
