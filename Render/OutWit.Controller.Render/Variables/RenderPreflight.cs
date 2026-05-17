using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping the unified render preflight result.
/// </summary>
[Variable("RenderPreflight")]
[MemoryPackable]
public sealed partial class WitVariableRenderPreflight : WitVariable<RenderPreflightData?>, IWitVariableFactory<WitVariableRenderPreflight>
{
    #region Constructors

    public WitVariableRenderPreflight(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderPreflight(string name, RenderPreflightData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderPreflight variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderPreflight Clone()
    {
        return new WitVariableRenderPreflight(Name, (RenderPreflightData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderPreflight Create(string name)
    {
        return new WitVariableRenderPreflight(name);
    }

    #endregion
}
