using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// The MinHash parameters of a pipeline: how many hash functions a signature has, how many words
/// make a shingle, and how many LSH bands the near-duplicate merge cuts a signature into. A value
/// of 0 is the controller's default (128 hashes, 5-word shingles, 16 bands), so a pipeline that
/// sets none of them signs exactly as before they were settings. In a result, the parameters the
/// node signed with, resolved.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusMinHashData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusMinHashData other)
            return false;

        return Hashes.Is(other.Hashes)
               && ShingleWords.Is(other.ShingleWords)
               && Bands.Is(other.Bands);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusMinHashData Clone()
    {
        return new CorpusMinHashData { Hashes = Hashes, ShingleWords = ShingleWords, Bands = Bands };
    }

    #endregion

    #region Properties

    /// <summary>Hash functions per signature (0 = 128); a signature is 8 bytes per hash.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int Hashes { get; set; }

    /// <summary>Words per shingle (0 = 5).</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int ShingleWords { get; set; }

    /// <summary>LSH bands of the near-duplicate merge (0 = 16); must divide the hash count.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public int Bands { get; set; }

    #endregion
}
