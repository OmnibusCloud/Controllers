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
               && PackageBytes.Is(other.PackageBytes)
               && ResultPackageBytes.Is(other.ResultPackageBytes)
               && InputBytes.Is(other.InputBytes)
               && Documents.Is(other.Documents)
               && SampledShards.Is(other.SampledShards)
               && UnsizedSources.Is(other.UnsizedSources)
               && UnsizedAtCap.Is(other.UnsizedAtCap)
               && ProductsKept.Is(other.ProductsKept)
               && MeasuredFilter.Is(other.MeasuredFilter)
               && ProductCeilingBytes.Is(other.ProductCeilingBytes);
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
            PackageBytes = PackageBytes,
            ResultPackageBytes = ResultPackageBytes,
            InputBytes = InputBytes,
            Documents = Documents,
            SampledShards = SampledShards,
            UnsizedSources = UnsizedSources,
            UnsizedAtCap = UnsizedAtCap,
            ProductsKept = ProductsKept,
            MeasuredFilter = MeasuredFilter,
            ProductCeilingBytes = ProductCeilingBytes
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

    /// <summary>The task packages and the result packages of every execution: the blobs the job's tasks and results travel in.</summary>
    [ToString]
    public long PackageBytes { get; init; }

    /// <summary>Sources with no size known: counted at the cap when <see cref="UnsizedAtCap"/>, left out otherwise.</summary>
    public int UnsizedSources { get; init; }

    /// <summary>Whether the sources with no size known are in the estimate, each at the cap the nodes hold a shard to.</summary>
    public bool UnsizedAtCap { get; init; }

    /// <summary>False when the quality filter is on under thresholds the ratios were not measured with (a manifest's own, a language pack's own): the estimate then takes the unfiltered ratios.</summary>
    public bool MeasuredFilter { get; init; } = true;

    /// <summary>Whether the run keeps its text and signatures on the server; false for statistics and hashes only.</summary>
    [ToString]
    public bool ProductsKept { get; init; } = true;

    /// <summary>Of <see cref="PackageBytes"/>, the result packages: the part the nodes upload (the task packages are the server's).</summary>
    public long ResultPackageBytes { get; init; }

    /// <summary>
    /// The most the products of the first round's executions may be, whoever wrote the manifest and
    /// whatever the origins serve: each shard at the most the nodes fetch it as (its declared size
    /// and the slack, the cap without a size) and its product at the most a shard of that size may
    /// give (<see cref="Shards.ShardProductLimits.MaxUploadBytes"/>); the largest packages twice for
    /// the sampled ones. A repair or a dispute uploads its products again, each held to the same
    /// ceiling. 0 for statistics and hashes only, and for shards left out of the estimate.
    /// </summary>
    public long ProductCeilingBytes { get; init; }

    /// <summary>What the nodes upload: the products of every execution (text, signatures, the second copies of the sampled shards) and the result packages. The server writes the task packages and the documents itself.</summary>
    public long UploadBytes => TextBytes + SignatureBytes + ReplicaBytes + ResultPackageBytes;

    /// <summary>Everything together.</summary>
    [ToString]
    public long TotalBytes => TextBytes + SignatureBytes + ReplicaBytes + DocumentBytes + PackageBytes + InputBytes;

    #endregion
}
