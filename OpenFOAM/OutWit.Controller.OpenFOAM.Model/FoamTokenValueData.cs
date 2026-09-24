using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// One substitution of a variant: the token as it stands in the templated
/// files and the text that replaces it. Values are text on purpose - the
/// initiator formats numbers, so a node never re-formats what the user saw.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamTokenValueData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamTokenValueData other)
            return false;

        return Token.Is(other.Token)
               && Value.Is(other.Value);
    }

    public override FoamTokenValueData Clone()
    {
        return new FoamTokenValueData
        {
            Token = Token,
            Value = Value
        };
    }

    public override string ToString()
    {
        return $"{Token} = {Value}";
    }

    #endregion

    #region Properties

    /// <summary>The token exactly as written in the templated files, e.g. <c>{{oc1}}</c>.</summary>
    [MemoryPackOrder(0)]
    public string Token { get; set; } = string.Empty;

    /// <summary>The replacement text.</summary>
    [MemoryPackOrder(1)]
    public string Value { get; set; } = string.Empty;

    #endregion
}
