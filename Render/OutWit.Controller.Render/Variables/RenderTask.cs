using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping a single render task description.
/// </summary>
[Variable("RenderTask")]
[MemoryPackable]
public sealed partial class WitVariableRenderTask : WitVariable<RenderTaskData?>, IWitVariableFactory<WitVariableRenderTask>
{
    #region Constructors

    public WitVariableRenderTask(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderTask(string name, RenderTaskData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderTask variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderTask Clone()
    {
        return new WitVariableRenderTask(Name, (RenderTaskData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderTask Create(string name)
    {
        return new WitVariableRenderTask(name);
    }

    #endregion
}
