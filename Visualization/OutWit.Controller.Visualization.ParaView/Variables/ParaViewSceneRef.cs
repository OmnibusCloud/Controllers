using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Variables;

/// <summary>
/// Variable wrapping a blob-backed OmnibusCloud ParaView package reference (state + attachments + requirements).
/// </summary>
[Variable("ParaViewSceneRef")]
[MemoryPackable]
public sealed partial class WitVariableParaViewSceneRef : WitVariable<ParaViewSceneRefData?>, IWitVariableFactory<WitVariableParaViewSceneRef>
{
    #region Constructors

    public WitVariableParaViewSceneRef(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableParaViewSceneRef(string name, ParaViewSceneRefData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewSceneRef variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableParaViewSceneRef Clone()
    {
        return new WitVariableParaViewSceneRef(Name, (ParaViewSceneRefData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableParaViewSceneRef Create(string name)
    {
        return new WitVariableParaViewSceneRef(name);
    }

    #endregion
}
