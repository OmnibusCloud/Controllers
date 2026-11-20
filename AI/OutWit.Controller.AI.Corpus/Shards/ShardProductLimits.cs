using System.Globalization;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The most one shard's product may be, as a function of the bytes fetched for it alone: its
/// documents and its compressed text. The signatures follow from the documents (one of a fixed
/// size each), so the two bound everything a node writes for a shard, uploads for it and leaves
/// on the server. A shard whose product passes a bound is <c>Oversized</c>
/// (<c>oversized_product</c>), the same on every node, and nothing of it is kept.
/// <para>
/// Without the bound a shard of a few hundred kilobytes made of one-word pages (one gzip member,
/// headers that repeat) gave 300,000 documents: 392 times its size on the node's disk with 128
/// hashes, 3,137 times with 1,024, where the disk question, the upload estimate and the storage
/// refusal were sized for crawl-like density. Common Crawl's own files give 180 to 370 documents
/// and 0.09 to 0.98 bytes of compressed text per fetched megabyte or byte
/// (<see cref="Storage.CorpusStorageRatios"/>); the bounds stand well above that, and above the
/// densest synthetic shard the tests make (about 1,300 documents of 300 words a mebibyte).
/// </para>
/// <para>
/// The bounds are what the node reserves on its disk for a shard (<see cref="ShardDisk.Needed"/>)
/// and what the preflight states as the most the uploads and the server's storage may be.
/// </para>
/// </summary>
public static class ShardProductLimits
{
    #region Constants

    /// <summary>The most documents a fetched mebibyte may give: one for every 512 bytes fetched (Common Crawl's files give 180 to 370 a mebibyte).</summary>
    public const int DOCUMENTS_PER_MIB = 2_048;

    /// <summary>The most documents any shard may give, however small it is packed.</summary>
    public const int MIN_DOCUMENTS = 4_096;

    /// <summary>The most bytes of compressed text a fetched byte may give (Common Crawl's files give 0.09 to 0.98).</summary>
    public const int TEXT_PER_BYTE = 2;

    /// <summary>The most bytes of compressed text any shard may give, however small it is packed.</summary>
    public const long MIN_TEXT_BYTES = 16L * 1024 * 1024;

    private const long BYTES_PER_MIB = 1024 * 1024;

    /// <summary>The bounds as one line: a part of the stage set's fingerprint, since a product past them is a verdict.</summary>
    public static readonly string DESCRIPTOR = string.Create(CultureInfo.InvariantCulture,
        $"documents<=max({MIN_DOCUMENTS}, {DOCUMENTS_PER_MIB}/MiB fetched); compressed text<=max({MIN_TEXT_BYTES}, {TEXT_PER_BYTE}x fetched) (past either: {Trust.CorpusResultCodes.OVERSIZED_PRODUCT})");

    #endregion

    #region Functions

    /// <summary>The most documents a shard of a fetched size may give.</summary>
    /// <param name="fetchedBytes">The bytes fetched for the shard.</param>
    /// <returns>The bound, <see cref="MIN_DOCUMENTS"/> at least.</returns>
    public static long MaxDocuments(long fetchedBytes)
    {
        var bytes = Math.Max(0, fetchedBytes);
        var byRatio = bytes / BYTES_PER_MIB * DOCUMENTS_PER_MIB + bytes % BYTES_PER_MIB * DOCUMENTS_PER_MIB / BYTES_PER_MIB;
        return Math.Max(MIN_DOCUMENTS, byRatio);
    }

    /// <summary>The most bytes of compressed text a shard of a fetched size may give.</summary>
    /// <param name="fetchedBytes">The bytes fetched for the shard.</param>
    /// <returns>The bound, <see cref="MIN_TEXT_BYTES"/> at least.</returns>
    public static long MaxTextBytes(long fetchedBytes)
    {
        var bytes = Math.Max(0, fetchedBytes);
        return Math.Max(MIN_TEXT_BYTES, bytes > long.MaxValue / TEXT_PER_BYTE ? long.MaxValue : bytes * TEXT_PER_BYTE);
    }

    /// <summary>The most bytes the signatures of a shard of a fetched size may be: the header and a signature for each document the shard may give.</summary>
    /// <param name="fetchedBytes">The bytes fetched for the shard.</param>
    /// <param name="minHash">The pipeline's MinHash parameters.</param>
    /// <returns>The bound.</returns>
    public static long MaxSignatureBytes(long fetchedBytes, MinHashParameters minHash)
    {
        return ShardSignatureWriter.HeaderSize(minHash) + MaxDocuments(fetchedBytes) * minHash.SignatureBytes;
    }

    /// <summary>The most bytes a shard's products may take on the node's disk: the compressed text and, when the pipeline signs, the signatures.</summary>
    /// <param name="fetchedBytes">The bytes fetched for the shard (or the most it may be).</param>
    /// <param name="pipeline">The pipeline; null for the default one (it signs, with 128 hashes).</param>
    /// <returns>The bound.</returns>
    public static long MaxProductBytes(long fetchedBytes, CorpusPipelineData? pipeline)
    {
        var text = MaxTextBytes(fetchedBytes);
        if (pipeline != null && !ShardProcessor.Signs(pipeline))
            return text;

        return text + MaxSignatureBytes(fetchedBytes, MinHashParameters.Resolve(pipeline?.MinHash));
    }

    /// <summary>The most bytes a shard's products send to the server: nothing under statistics and hashes only, the text and the kept signatures otherwise.</summary>
    /// <param name="fetchedBytes">The bytes fetched for the shard (or the most it may be).</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <returns>The bound.</returns>
    public static long MaxUploadBytes(long fetchedBytes, CorpusPipelineData pipeline)
    {
        if (pipeline.Output != CorpusOutputMode.Full)
            return 0;

        var text = MaxTextBytes(fetchedBytes);
        return ShardProcessor.KeepsSignatures(pipeline) ? text + MaxSignatureBytes(fetchedBytes, MinHashParameters.Resolve(pipeline.MinHash)) : text;
    }

    #endregion
}
