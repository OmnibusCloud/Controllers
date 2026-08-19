using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Variables;

/// <summary>
/// Variable wrapping the output options of a ParaView render (view, size, format, frame selection).
/// </summary>
[Variable("ParaViewOutputOptions")]
[MemoryPackable]
public sealed partial class WitVariableParaViewOutputOptions : WitVariable<ParaViewOutputOptionsData?>, IWitVariableFactory<WitVariableParaViewOutputOptions>
{
    #region Constructors

    public WitVariableParaViewOutputOptions(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableParaViewOutputOptions(string name, ParaViewOutputOptionsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableParaViewOutputOptions variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableParaViewOutputOptions Clone()
    {
        return new WitVariableParaViewOutputOptions(Name, (ParaViewOutputOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableParaViewOutputOptions Create(string name)
    {
        return new WitVariableParaViewOutputOptions(name);
    }

    #endregion
}
