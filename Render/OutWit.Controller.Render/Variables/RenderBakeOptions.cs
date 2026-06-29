using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping <see cref="RenderBakeOptionsData"/> for Render.BakeSimulation.
/// </summary>
[Variable("RenderBakeOptions")]
[MemoryPackable]
public sealed partial class WitVariableRenderBakeOptions : WitVariable<RenderBakeOptionsData?>, IWitVariableFactory<WitVariableRenderBakeOptions>
{
    #region Constructors

    public WitVariableRenderBakeOptions(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderBakeOptions(string name, RenderBakeOptionsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderBakeOptions variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderBakeOptions Clone()
    {
        return new WitVariableRenderBakeOptions(Name, (RenderBakeOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderBakeOptions Create(string name)
    {
        return new WitVariableRenderBakeOptions(name);
    }

    #endregion
}
