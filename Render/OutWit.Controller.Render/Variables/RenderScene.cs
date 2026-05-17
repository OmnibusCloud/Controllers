using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping an inline bootstrap typed-scene payload.
/// </summary>
[Variable("RenderScene")]
[MemoryPackable]
public sealed partial class WitVariableRenderScene : WitVariable<RenderSceneData?>, IWitVariableFactory<WitVariableRenderScene>
{
    #region Constructors

    public WitVariableRenderScene(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderScene(string name, RenderSceneData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderScene variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderScene Clone()
    {
        return new WitVariableRenderScene(Name, (RenderSceneData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderScene Create(string name)
    {
        return new WitVariableRenderScene(name);
    }

    #endregion
}
