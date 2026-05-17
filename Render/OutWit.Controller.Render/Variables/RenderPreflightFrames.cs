using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping frame-render preflight validation results.
/// </summary>
[Variable("RenderPreflightFrames")]
[MemoryPackable]
public sealed partial class WitVariableRenderPreflightFrames : WitVariable<RenderPreflightFramesData?>, IWitVariableFactory<WitVariableRenderPreflightFrames>
{
    #region Constructors

    public WitVariableRenderPreflightFrames(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderPreflightFrames(string name, RenderPreflightFramesData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderPreflightFrames variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderPreflightFrames Clone()
    {
        return new WitVariableRenderPreflightFrames(Name, (RenderPreflightFramesData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderPreflightFrames Create(string name)
    {
        return new WitVariableRenderPreflightFrames(name);
    }

    #endregion
}
