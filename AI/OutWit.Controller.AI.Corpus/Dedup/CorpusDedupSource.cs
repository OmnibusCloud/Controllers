using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// One completed shard the merge reads, with what the report says its signatures blob holds: the
/// merge uses a blob only when its bytes are the ones integrity compared and it holds one
/// signature per document of the product.
/// </summary>
/// <param name="Index">The shard's task index.</param>
/// <param name="SignaturesBlobId">Its signatures blob.</param>
/// <param name="Parameters">The MinHash parameters the report says they were made with.</param>
/// <param name="SignaturesSha256">The SHA-256 of the blob's bytes as the report states it (<c>signatures_sha256</c>); "" for a report of a controller before 0.3.0, which names none.</param>
/// <param name="Documents">The documents of the shard's product (<c>documents_out</c>): the signatures the blob must hold.</param>
public sealed record CorpusDedupSource(int Index, Guid SignaturesBlobId, MinHashParameters Parameters, string SignaturesSha256, int Documents)
{
    #region Properties

    /// <summary>The bytes a blob with this many signatures of these parameters has, header included.</summary>
    public long ExpectedBytes => ShardSignatureWriter.HeaderSize(Parameters) + (long)Documents * Parameters.SignatureBytes;

    #endregion
}
