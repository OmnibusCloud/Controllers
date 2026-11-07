using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.CalculiX.Model;

/// <summary>
/// One named response value extracted from a solve.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class CcxResponseValueData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CcxResponseValueData value)
            return false;

        return Name.Is(value.Name)
               && Value.Is(value.Value, tolerance);
    }

    public override CcxResponseValueData Clone()
    {
        return new CcxResponseValueData
        {
            Name = Name,
            Value = Value
        };
    }

    public override string ToString()
    {
        return $"{Name} = {Value}";
    }

    #endregion

    #region Properties

    /// <summary>Response name, e.g. "max_von_mises".</summary>
    [MemoryPackOrder(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Extracted value.</summary>
    [MemoryPackOrder(1)]
    public double Value { get; set; }

    #endregion
}
