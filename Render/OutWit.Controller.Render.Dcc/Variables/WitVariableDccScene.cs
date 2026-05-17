using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Variables;

/// <summary>
/// Variable wrapping an inline neutral DCC scene payload.
/// </summary>
[Variable("DccScene")]
[MemoryPackable]
public sealed partial class WitVariableDccScene : WitVariable<DccSceneData?>, IWitVariableFactory<WitVariableDccScene>
{
    #region Constructors

    public WitVariableDccScene(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableDccScene(string name, DccSceneData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableDccScene variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableDccScene Clone()
    {
        return new WitVariableDccScene(Name, (DccSceneData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableDccScene Create(string name)
    {
        return new WitVariableDccScene(name);
    }

    #endregion
}
