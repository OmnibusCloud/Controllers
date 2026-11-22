namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// A completed shard the merge left out, and why: its signatures blob is not what the report says
/// of it. One such shard costs the merge that shard, never the run; its documents are in no mask
/// and no cluster, and the dedup document names it.
/// </summary>
/// <param name="Index">The shard's task index.</param>
/// <param name="SignaturesBlobId">The blob that was not used.</param>
/// <param name="Reason">Why, a fixed code: one of <see cref="REASONS"/>.</param>
public sealed record CorpusDedupExclusion(int Index, Guid SignaturesBlobId, string Reason)
{
    #region Constants

    /// <summary>The blob is not in the store, or cannot be read.</summary>
    public const string BLOB_MISSING = "blob_missing";

    /// <summary>The blob's length is not that of one signature per document of the product.</summary>
    public const string BLOB_LENGTH = "blob_length";

    /// <summary>The blob does not start as a signatures blob of the parameters the report states.</summary>
    public const string BLOB_HEADER = "blob_header";

    /// <summary>The blob's bytes do not hash to the <c>signatures_sha256</c> the report states: not the signatures integrity compared.</summary>
    public const string BLOB_HASH = "blob_hash";

    /// <summary>The shard's signatures were made with other MinHash parameters than the run's.</summary>
    public const string PARAMETERS = "parameters";

    /// <summary>Every reason a shard is left out for.</summary>
    public static readonly IReadOnlyList<string> REASONS = [BLOB_MISSING, BLOB_LENGTH, BLOB_HEADER, BLOB_HASH, PARAMETERS];

    #endregion
}
