using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping a single render-task chunk (the Grid.ForEach iteration item over a
/// RenderTaskBatchCollection; input to Render.FrameBatch).
/// </summary>
[Variable("RenderTaskBatch")]
[MemoryPackable]
public sealed partial class WitVariableRenderTaskBatch : WitVariable<RenderTaskBatchData?>, IWitVariableFactory<WitVariableRenderTaskBatch>
{
    #region Constructors

    public WitVariableRenderTaskBatch(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderTaskBatch(string name, RenderTaskBatchData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderTaskBatch variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderTaskBatch Clone()
    {
        return new WitVariableRenderTaskBatch(Name, (RenderTaskBatchData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderTaskBatch Create(string name)
    {
        return new WitVariableRenderTaskBatch(name);
    }

    #endregion
}
