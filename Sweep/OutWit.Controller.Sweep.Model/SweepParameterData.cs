using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// One swept parameter: a display name and the placeholder token the
/// initiator baked into the study's input in place of the original literal
/// (a CalculiX base deck, an OpenFOAM case's templated files).
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepParameterData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepParameterData parameter)
            return false;

        return Name.Is(parameter.Name)
               && Token.Is(parameter.Token);
    }

    public override SweepParameterData Clone()
    {
        return new SweepParameterData
        {
            Name = Name,
            Token = Token
        };
    }

    public override string ToString()
    {
        return $"{Name} ({Token})";
    }

    #endregion

    #region Properties

    /// <summary>Display name of the parameter.</summary>
    [MemoryPackOrder(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Placeholder token present in the study's input, e.g. "{{oc1}}".</summary>
    [MemoryPackOrder(1)]
    public string Token { get; set; } = string.Empty;

    #endregion
}
