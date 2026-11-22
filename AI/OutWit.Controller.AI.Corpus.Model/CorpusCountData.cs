using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// A named count: a drop rule and how many documents it dropped, a language and how many
/// documents carry it.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusCountData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusCountData other)
            return false;

        return Name.Is(other.Name) && Count.Is(other.Count);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusCountData Clone()
    {
        return new CorpusCountData { Name = Name, Count = Count };
    }

    #endregion

    #region Properties

    /// <summary>The name (a rule, a language code, ...).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public string Name { get; set; } = "";

    /// <summary>The count.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int Count { get; set; }

    #endregion
}
