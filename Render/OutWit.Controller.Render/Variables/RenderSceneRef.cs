using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping a blob-backed bootstrap typed-scene reference.
/// </summary>
[Variable("RenderSceneRef")]
[MemoryPackable]
public sealed partial class WitVariableRenderSceneRef : WitVariable<RenderSceneRefData?>, IWitVariableFactory<WitVariableRenderSceneRef>
{
    #region Constructors

    public WitVariableRenderSceneRef(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableRenderSceneRef(string name, RenderSceneRefData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableRenderSceneRef variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableRenderSceneRef Clone()
    {
        return new WitVariableRenderSceneRef(Name, (RenderSceneRefData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableRenderSceneRef Create(string name)
    {
        return new WitVariableRenderSceneRef(name);
    }

    #endregion
}
