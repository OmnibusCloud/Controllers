using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Variables;

/// <summary>
/// Variable wrapping one ParaView render batch: a chunk of outputs one pvpython process renders together (FrameBatch, Model 0.5.0).
/// </summary>
[Variable("ParaViewRenderTaskBatch")]
[MemoryPackable]
public sealed partial class WitVariableParaViewRenderTaskBatch : WitVariable<ParaViewRenderTaskBatchData?>, IWitVariableFactory<WitVariableParaViewRenderTaskBatch>
{
    #region Constructors

    /// <summary>
    /// Creates an empty variable.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    public WitVariableParaViewRenderTaskBatch(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Creates a variable holding a value.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <param name=\"value\">Initial value.</param>
    [MemoryPackConstructor]
    public WitVariableParaViewRenderTaskBatch(string name, ParaViewRenderTaskBatchData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewRenderTaskBatch variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    /// <inheritdoc />
    public override WitVariableParaViewRenderTaskBatch Clone()
    {
        return new WitVariableParaViewRenderTaskBatch(Name, (ParaViewRenderTaskBatchData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Creates an empty variable (factory contract of the engine).
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <returns>The variable.</returns>
    public static WitVariableParaViewRenderTaskBatch Create(string name)
    {
        return new WitVariableParaViewRenderTaskBatch(name);
    }

    #endregion
}
