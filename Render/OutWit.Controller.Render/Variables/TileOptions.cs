using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Variables;

/// <summary>
/// Variable wrapping tile-specific options.
/// </summary>
[Variable("TileOptions")]
[MemoryPackable]
public sealed partial class WitVariableTileOptions : WitVariable<TileOptionsData?>, IWitVariableFactory<WitVariableTileOptions>
{
    #region Constructors

    public WitVariableTileOptions(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableTileOptions(string name, TileOptionsData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitVariableTileOptions variable)
            return false;

        var value = GetValue();
        var otherValue = variable.GetValue();

        return base.Is(modelBase, tolerance)
               && ((value == null && otherValue == null)
                   || (value != null && otherValue != null && value.Is(otherValue, tolerance)));
    }

    public override WitVariableTileOptions Clone()
    {
        return new WitVariableTileOptions(Name, (TileOptionsData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableTileOptions Create(string name)
    {
        return new WitVariableTileOptions(name);
    }

    #endregion
}
