namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>One shard's signatures as the merge reads them: product order, one per document.</summary>
/// <param name="ShardIndex">The shard's task index.</param>
/// <param name="SignaturesBlobId">The blob the signatures came from (for the document).</param>
/// <param name="Signatures">The MinHash signatures in product order.</param>
public sealed record CorpusDedupShard(int ShardIndex, Guid SignaturesBlobId, IReadOnlyList<ulong[]> Signatures);
