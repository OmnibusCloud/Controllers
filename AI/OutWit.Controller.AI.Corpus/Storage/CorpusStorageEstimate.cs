using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Storage;

/// <summary>
/// What a job will leave in the server's blob store for the retention window, part by part:
/// the text products and the signatures of every shard once, the second copies of the sampled
/// shards (each execution uploads its own products), the job's documents (report, integrity,
/// dedup) and its inputs (manifest, language model).
/// </summary>
public sealed class CorpusStorageEstimate : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusStorageEstimate other)
            return false;

        return TextBytes.Is(other.TextBytes)
               && SignatureBytes.Is(other.SignatureBytes)
               && ReplicaBytes.Is(other.ReplicaBytes)
               && DocumentBytes.Is(other.DocumentBytes)
               && InputBytes.Is(other.InputBytes)
               && Documents.Is(other.Documents)
               && SampledShards.Is(other.SampledShards)
               && UnsizedSources.Is(other.UnsizedSources);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusStorageEstimate Clone()
    {
        return new CorpusStorageEstimate
        {
            TextBytes = TextBytes,
            SignatureBytes = SignatureBytes,
            ReplicaBytes = ReplicaBytes,
            DocumentBytes = DocumentBytes,
            InputBytes = InputBytes,
            Documents = Documents,
            SampledShards = SampledShards,
            UnsizedSources = UnsizedSources
        };
    }

    #endregion

    #region Properties

    /// <summary>The compressed text products, every shard once.</summary>
    [ToString]
    public long TextBytes { get; init; }

    /// <summary>The signatures blobs, every shard once (0 when the pipeline does not sign).</summary>
    [ToString]
    public long SignatureBytes { get; init; }

    /// <summary>The second products of the sampled shards: the largest shards are assumed sampled.</summary>
    [ToString]
    public long ReplicaBytes { get; init; }

    /// <summary>The report, the integrity document and the dedup document.</summary>
    [ToString]
    public long DocumentBytes { get; init; }

    /// <summary>The manifest and the language model, already in the store.</summary>
    [ToString]
    public long InputBytes { get; init; }

    /// <summary>The documents the products are estimated to hold.</summary>
    public long Documents { get; init; }

    /// <summary>Shards that run twice at the job's sampling rate.</summary>
    public int SampledShards { get; init; }

    /// <summary>Sources with no size known: they are not in the estimate.</summary>
    public int UnsizedSources { get; init; }

    /// <summary>Everything together.</summary>
    [ToString]
    public long TotalBytes => TextBytes + SignatureBytes + ReplicaBytes + DocumentBytes + InputBytes;

    #endregion
}
