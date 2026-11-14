using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// The quality thresholds of a pipeline (the Gopher-style shape and repetition rules) and its
/// optional quality rules. A threshold of 0 means "the stage default". Part of the pipeline
/// contract: change a threshold, change the pipeline version.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusQualityData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusQualityData other)
            return false;

        return MinWords.Is(other.MinWords)
               && MaxWords.Is(other.MaxWords)
               && MinMeanWordLength.Is(other.MinMeanWordLength, tolerance)
               && MaxMeanWordLength.Is(other.MaxMeanWordLength, tolerance)
               && MaxSymbolToWordRatio.Is(other.MaxSymbolToWordRatio, tolerance)
               && MaxBulletLineRatio.Is(other.MaxBulletLineRatio, tolerance)
               && MaxEllipsisLineRatio.Is(other.MaxEllipsisLineRatio, tolerance)
               && MinAlphabeticWordRatio.Is(other.MinAlphabeticWordRatio, tolerance)
               && MaxDuplicateLineRatio.Is(other.MaxDuplicateLineRatio, tolerance)
               && MaxDuplicateLineCharRatio.Is(other.MaxDuplicateLineCharRatio, tolerance)
               && FilterCurlyBracket.Is(other.FilterCurlyBracket);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusQualityData Clone()
    {
        return new CorpusQualityData
        {
            MinWords = MinWords,
            MaxWords = MaxWords,
            MinMeanWordLength = MinMeanWordLength,
            MaxMeanWordLength = MaxMeanWordLength,
            MaxSymbolToWordRatio = MaxSymbolToWordRatio,
            MaxBulletLineRatio = MaxBulletLineRatio,
            MaxEllipsisLineRatio = MaxEllipsisLineRatio,
            MinAlphabeticWordRatio = MinAlphabeticWordRatio,
            MaxDuplicateLineRatio = MaxDuplicateLineRatio,
            MaxDuplicateLineCharRatio = MaxDuplicateLineCharRatio,
            FilterCurlyBracket = FilterCurlyBracket
        };
    }

    #endregion

    #region Properties

    /// <summary>Fewest words a document may have.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int MinWords { get; set; }

    /// <summary>Most words a document may have.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int MaxWords { get; set; }

    /// <summary>Lowest mean word length.</summary>
    [MemoryPackOrder(2)]
    public double MinMeanWordLength { get; set; }

    /// <summary>Highest mean word length.</summary>
    [MemoryPackOrder(3)]
    public double MaxMeanWordLength { get; set; }

    /// <summary>Most hash marks and ellipses per word.</summary>
    [MemoryPackOrder(4)]
    public double MaxSymbolToWordRatio { get; set; }

    /// <summary>Most lines that may start with a bullet.</summary>
    [MemoryPackOrder(5)]
    public double MaxBulletLineRatio { get; set; }

    /// <summary>Most lines that may end with an ellipsis.</summary>
    [MemoryPackOrder(6)]
    public double MaxEllipsisLineRatio { get; set; }

    /// <summary>Fewest words that must carry a letter.</summary>
    [MemoryPackOrder(7)]
    public double MinAlphabeticWordRatio { get; set; }

    /// <summary>Most lines that repeat an earlier line.</summary>
    [MemoryPackOrder(8)]
    public double MaxDuplicateLineRatio { get; set; }

    /// <summary>Most characters in lines that repeat an earlier line.</summary>
    [MemoryPackOrder(9)]
    public double MaxDuplicateLineCharRatio { get; set; }

    /// <summary>Whether a document goes when a line C4 would test holds a curly bracket (C4's rule; the manifest's <c>pipeline.filter_curly_bracket</c>, off by default).</summary>
    [ToString]
    [MemoryPackOrder(10)]
    public bool FilterCurlyBracket { get; set; }

    #endregion
}
