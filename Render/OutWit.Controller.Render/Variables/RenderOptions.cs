using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping render options.
/// </summary>
[Variable("RenderOptions")]
[MemoryPackable]
public sealed partial class WitVariableRenderOptions : WitVariable<RenderOptionsData?>, IWitVariableFactory<WitVariableRenderOptions>
{
    #region Constructors

    public WitVariableRenderOptions(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderOptions(string name, RenderOptionsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderOptions variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderOptions Clone()
    {
        return new WitVariableRenderOptions(Name, (RenderOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderOptions Create(string name)
    {
        return new WitVariableRenderOptions(name);
    }

    #endregion
}
