using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// One named number read back from a run: a response column, a final
/// residual, a coefficient.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamResponseValueData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamResponseValueData value)
            return false;

        return Name.Is(value.Name)
               && Value.Is(value.Value, tolerance);
    }

    public override FoamResponseValueData Clone()
    {
        return new FoamResponseValueData
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

    /// <summary>The name, e.g. <c>coeffs.Cd</c> or <c>residual.Ux</c>.</summary>
    [MemoryPackOrder(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The value.</summary>
    [MemoryPackOrder(1)]
    public double Value { get; set; }

    #endregion
}
