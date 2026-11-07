using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// A named text value, as OpenFOAM dictionaries hold them: the reference
/// values of a force-coefficient request (<c>magUInf 20</c>, <c>lRef 1.42</c>,
/// <c>liftDir (0 0 1)</c>) travel this way, unparsed, and are written into
/// the function-object dictionary verbatim.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamNamedValueData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamNamedValueData other)
            return false;

        return Name.Is(other.Name)
               && Value.Is(other.Value);
    }

    public override FoamNamedValueData Clone()
    {
        return new FoamNamedValueData
        {
            Name = Name,
            Value = Value
        };
    }

    public override string ToString()
    {
        return $"{Name} {Value}";
    }

    #endregion

    #region Properties

    /// <summary>The keyword.</summary>
    [MemoryPackOrder(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The entry text, as it stands in a dictionary (without the trailing semicolon).</summary>
    [MemoryPackOrder(1)]
    public string Value { get; set; } = string.Empty;

    #endregion
}
