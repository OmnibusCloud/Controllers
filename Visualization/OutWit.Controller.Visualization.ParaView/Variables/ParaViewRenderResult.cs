using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Variables;

/// <summary>
/// Variable wrapping the result of one ParaView render task.
/// </summary>
[Variable("ParaViewRenderResult")]
[MemoryPackable]
public sealed partial class WitVariableParaViewRenderResult : WitVariable<ParaViewRenderResultData?>, IWitVariableFactory<WitVariableParaViewRenderResult>
{
    #region Constructors

    public WitVariableParaViewRenderResult(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableParaViewRenderResult(string name, ParaViewRenderResultData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewRenderResult variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableParaViewRenderResult Clone()
    {
        return new WitVariableParaViewRenderResult(Name, (ParaViewRenderResultData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableParaViewRenderResult Create(string name)
    {
        return new WitVariableParaViewRenderResult(name);
    }

    #endregion
}
