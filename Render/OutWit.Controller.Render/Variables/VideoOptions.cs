using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping video encoding options.
/// </summary>
[Variable("VideoOptions")]
[MemoryPackable]
public sealed partial class WitVariableVideoOptions : WitVariable<VideoOptionsData?>, IWitVariableFactory<WitVariableVideoOptions>
{
    #region Constructors

    public WitVariableVideoOptions(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableVideoOptions(string name, VideoOptionsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableVideoOptions variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableVideoOptions Clone()
    {
        return new WitVariableVideoOptions(Name, (VideoOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableVideoOptions Create(string name)
    {
        return new WitVariableVideoOptions(name);
    }

    #endregion
}
