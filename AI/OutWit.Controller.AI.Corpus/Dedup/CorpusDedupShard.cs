using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// One shard's signatures as the merge reads them: product order, one per document, in one flat
/// array (document i's signature is the <see cref="Hashes"/> values from i × <see cref="Hashes"/>),
/// so a shard costs its values and nothing per document.
/// </summary>
/// <param name="ShardIndex">The shard's task index.</param>
/// <param name="SignaturesBlobId">The blob the signatures came from (for the document).</param>
/// <param name="Hashes">Values per signature; at least 1.</param>
/// <param name="Values">Every signature of the shard, one after another.</param>
public sealed record CorpusDedupShard(int ShardIndex, Guid SignaturesBlobId, int Hashes, ulong[] Values)
{
    #region Functions

    /// <summary>A shard from one array per document (tests and tools).</summary>
    /// <param name="shardIndex">The shard's task index.</param>
    /// <param name="signaturesBlobId">The blob the signatures came from.</param>
    /// <param name="signatures">The signatures in product order, all of one length.</param>
    /// <returns>The shard.</returns>
    /// <exception cref="ArgumentException">The signatures differ in length.</exception>
    public static CorpusDedupShard Of(int shardIndex, Guid signaturesBlobId, IReadOnlyList<ulong[]> signatures)
    {
        var hashes = signatures.Count > 0 ? signatures[0].Length : MinHashSigner.HASHES;
        if (hashes <= 0 || signatures.Any(signature => signature.Length != hashes))
            throw new ArgumentException("the signatures of a shard have one length", nameof(signatures));

        var values = new ulong[(long)signatures.Count * hashes];
        for (var document = 0; document < signatures.Count; document++)
            signatures[document].CopyTo(values, (long)document * hashes);

        return new CorpusDedupShard(shardIndex, signaturesBlobId, hashes, values);
    }

    /// <summary>One document's signature.</summary>
    /// <param name="document">The document's ordinal in the product.</param>
    /// <returns>Its values.</returns>
    public ReadOnlySpan<ulong> Signature(int document)
    {
        return Values.AsSpan(document * Hashes, Hashes);
    }

    #endregion

    #region Properties

    /// <summary>Documents in the shard.</summary>
    public int Documents => Values.Length / Math.Max(1, Hashes);

    #endregion
}
