using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Storage;

/// <summary>
/// What one shard leaves behind per byte fetched, for one input format and one setting of
/// the two switches that change it most (the boilerplate-line stage and the quality filter):
/// the bytes of the compressed text product, and the documents the product holds (each
/// document is one signature in the signatures blob). A measured maximum with a margin.
/// </summary>
public sealed class CorpusStorageRatio : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusStorageRatio other)
            return false;

        return Format.Is(other.Format)
               && CleanLines.Is(other.CleanLines)
               && FilterQuality.Is(other.FilterQuality)
               && TextPerByte.Is(other.TextPerByte, tolerance)
               && DocumentsPerMib.Is(other.DocumentsPerMib, tolerance);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusStorageRatio Clone()
    {
        return new CorpusStorageRatio
        {
            Format = Format,
            CleanLines = CleanLines,
            FilterQuality = FilterQuality,
            TextPerByte = TextPerByte,
            DocumentsPerMib = DocumentsPerMib
        };
    }

    #endregion

    #region Properties

    /// <summary>The input format.</summary>
    [ToString]
    public CorpusSourceFormat Format { get; init; }

    /// <summary>Whether the boilerplate-line stage runs.</summary>
    [ToString]
    public bool CleanLines { get; init; }

    /// <summary>Whether the quality filter runs.</summary>
    [ToString]
    public bool FilterQuality { get; init; }

    /// <summary>Bytes of compressed text product per byte of shard on the wire.</summary>
    [ToString]
    public double TextPerByte { get; init; }

    /// <summary>Documents in the product per MiB of shard on the wire.</summary>
    [ToString]
    public double DocumentsPerMib { get; init; }

    #endregion
}
