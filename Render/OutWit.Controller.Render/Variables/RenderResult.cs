using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping a single render result.
/// </summary>
[Variable("RenderResult")]
[MemoryPackable]
public sealed partial class WitVariableRenderResult : WitVariable<RenderResultData?>, IWitVariableFactory<WitVariableRenderResult>
{
    #region Constructors

    public WitVariableRenderResult(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderResult(string name, RenderResultData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderResult variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderResult Clone()
    {
        return new WitVariableRenderResult(Name, (RenderResultData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderResult Create(string name)
    {
        return new WitVariableRenderResult(name);
    }

    #endregion
}
