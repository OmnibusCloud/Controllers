using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>One completed shard the merge reads.</summary>
/// <param name="Index">The shard's task index.</param>
/// <param name="SignaturesBlobId">Its signatures blob.</param>
/// <param name="Parameters">The MinHash parameters the report says they were made with.</param>
public sealed record CorpusDedupSource(int Index, Guid SignaturesBlobId, MinHashParameters Parameters);
