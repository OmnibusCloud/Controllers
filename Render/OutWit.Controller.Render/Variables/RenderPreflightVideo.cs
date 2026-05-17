using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping video preflight validation results.
/// </summary>
[Variable("RenderPreflightVideo")]
[MemoryPackable]
public sealed partial class WitVariableRenderPreflightVideo : WitVariable<RenderPreflightVideoData?>, IWitVariableFactory<WitVariableRenderPreflightVideo>
{
    #region Constructors

    public WitVariableRenderPreflightVideo(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderPreflightVideo(string name, RenderPreflightVideoData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderPreflightVideo variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderPreflightVideo Clone()
    {
        return new WitVariableRenderPreflightVideo(Name, (RenderPreflightVideoData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderPreflightVideo Create(string name)
    {
        return new WitVariableRenderPreflightVideo(name);
    }

    #endregion
}
