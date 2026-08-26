using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Variables;

/// <summary>
/// Variable wrapping the result of one ParaView render task.
/// </summary>
[Variable("ParaViewRenderResultBatch")]
[MemoryPackable]
public sealed partial class WitVariableParaViewRenderResultBatch : WitVariable<ParaViewRenderResultBatchData?>, IWitVariableFactory<WitVariableParaViewRenderResultBatch>
{
    #region Constructors

    /// <summary>
    /// Creates an empty variable.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    public WitVariableParaViewRenderResultBatch(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Creates a variable holding a value.
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <param name=\"value\">Initial value.</param>
    [MemoryPackConstructor]
    public WitVariableParaViewRenderResultBatch(string name, ParaViewRenderResultBatchData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewRenderResultBatch variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    /// <inheritdoc />
    public override WitVariableParaViewRenderResultBatch Clone()
    {
        return new WitVariableParaViewRenderResultBatch(Name, (ParaViewRenderResultBatchData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Creates an empty variable (factory contract of the engine).
    /// </summary>
    /// <param name=\"name\">Variable name.</param>
    /// <returns>The variable.</returns>
    public static WitVariableParaViewRenderResultBatch Create(string name)
    {
        return new WitVariableParaViewRenderResultBatch(name);
    }

    #endregion
}
