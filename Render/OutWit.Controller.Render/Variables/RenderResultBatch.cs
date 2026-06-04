using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping the result of one Render.FrameBatch (the per-frame/tile results of a chunk);
/// the Grid.ForEach iteration result, collected into a RenderResultBatchCollection.
/// </summary>
[Variable("RenderResultBatch")]
[MemoryPackable]
public sealed partial class WitVariableRenderResultBatch : WitVariable<RenderResultBatchData?>, IWitVariableFactory<WitVariableRenderResultBatch>
{
    #region Constructors

    public WitVariableRenderResultBatch(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderResultBatch(string name, RenderResultBatchData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderResultBatch variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderResultBatch Clone()
    {
        return new WitVariableRenderResultBatch(Name, (RenderResultBatchData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderResultBatch Create(string name)
    {
        return new WitVariableRenderResultBatch(name);
    }

    #endregion
}
