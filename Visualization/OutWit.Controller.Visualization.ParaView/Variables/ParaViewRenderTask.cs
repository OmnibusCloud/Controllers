using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Variables;

/// <summary>
/// Variable wrapping a single ParaView render task (one view, one timestep, its attachment subset).
/// </summary>
[Variable("ParaViewRenderTask")]
[MemoryPackable]
public sealed partial class WitVariableParaViewRenderTask : WitVariable<ParaViewRenderTaskData?>, IWitVariableFactory<WitVariableParaViewRenderTask>
{
    #region Constructors

    public WitVariableParaViewRenderTask(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableParaViewRenderTask(string name, ParaViewRenderTaskData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewRenderTask variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableParaViewRenderTask Clone()
    {
        return new WitVariableParaViewRenderTask(Name, (ParaViewRenderTaskData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableParaViewRenderTask Create(string name)
    {
        return new WitVariableParaViewRenderTask(name);
    }

    #endregion
}
