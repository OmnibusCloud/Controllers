using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping tiled-still preflight validation results.
/// </summary>
[Variable("RenderPreflightStillTiled")]
[MemoryPackable]
public sealed partial class WitVariableRenderPreflightStillTiled : WitVariable<RenderPreflightStillTiledData?>, IWitVariableFactory<WitVariableRenderPreflightStillTiled>
{
    #region Constructors

    public WitVariableRenderPreflightStillTiled(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderPreflightStillTiled(string name, RenderPreflightStillTiledData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderPreflightStillTiled variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderPreflightStillTiled Clone()
    {
        return new WitVariableRenderPreflightStillTiled(Name, (RenderPreflightStillTiledData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderPreflightStillTiled Create(string name)
    {
        return new WitVariableRenderPreflightStillTiled(name);
    }

    #endregion
}
