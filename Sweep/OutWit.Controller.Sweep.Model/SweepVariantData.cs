using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// One row of the variant table: its stable index and its substitution
/// values, one per parameter, in the parameter list's order. What a variant
/// runs on beyond its values is the family block's business (a deck set's
/// per-variant decks live in <see cref="SweepCalculiXStudyData"/>).
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepVariantData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepVariantData variant)
            return false;

        return VariantIndex.Is(variant.VariantIndex)
               && Values.Is(variant.Values);
    }

    public override SweepVariantData Clone()
    {
        return new SweepVariantData
        {
            VariantIndex = VariantIndex,
            Values = [.. Values]
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: [{string.Join(", ", Values)}]";
    }

    #endregion

    #region Properties

    /// <summary>
    /// Stable index of the variant in the study. Every result is mapped by it,
    /// never by position: a node returns results in completion order.
    /// </summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>Substitution values, ordered like the parameter list; empty in a study without parameters (a deck set).</summary>
    [MemoryPackOrder(1)]
    public List<string> Values { get; set; } = [];

    #endregion
}
