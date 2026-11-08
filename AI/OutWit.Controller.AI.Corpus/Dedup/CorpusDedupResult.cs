namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// What the merge decided: per shard a keep mask in product order (false = a near-duplicate
/// of an earlier document), and the clusters of two or more documents as (shard, ordinal)
/// pairs with the representative first.
/// </summary>
/// <param name="Threshold">The Jaccard estimate a pair needed to join a cluster.</param>
/// <param name="Bands">The LSH bands used.</param>
/// <param name="Documents">Documents seen.</param>
/// <param name="Shards">Per shard: index, document count, duplicates, keep mask.</param>
/// <param name="Clusters">Clusters of two or more, each in (shard, ordinal) order, the representative first.</param>
public sealed record CorpusDedupResult(double Threshold, int Bands, int Documents, IReadOnlyList<CorpusDedupShardResult> Shards, IReadOnlyList<IReadOnlyList<CorpusDocumentRef>> Clusters)
{
    #region Properties

    /// <summary>Documents that are near-duplicates of an earlier one.</summary>
    public int Duplicates => Shards.Sum(shard => shard.Duplicates);

    #endregion
}

/// <summary>One shard's share of the decision.</summary>
/// <param name="ShardIndex">The shard's task index.</param>
/// <param name="SignaturesBlobId">The signatures blob the decision was made from.</param>
/// <param name="Keep">In product order: true to keep the document.</param>
public sealed record CorpusDedupShardResult(int ShardIndex, Guid SignaturesBlobId, bool[] Keep)
{
    #region Properties

    /// <summary>Documents in the shard.</summary>
    public int Documents => Keep.Length;

    /// <summary>Documents the mask drops.</summary>
    public int Duplicates => Keep.Count(keep => !keep);

    #endregion
}

/// <summary>A document by its shard and its ordinal in the shard's product (the i-th JSON line, the i-th signature).</summary>
/// <param name="ShardIndex">The shard's task index.</param>
/// <param name="Ordinal">The document's position in the shard's product, from 0.</param>
public readonly record struct CorpusDocumentRef(int ShardIndex, int Ordinal) : IComparable<CorpusDocumentRef>
{
    #region IComparable

    /// <summary>Shard first, then ordinal.</summary>
    /// <param name="other">The other document.</param>
    /// <returns>The usual sign.</returns>
    public int CompareTo(CorpusDocumentRef other)
    {
        return ShardIndex != other.ShardIndex ? ShardIndex.CompareTo(other.ShardIndex) : Ordinal.CompareTo(other.Ordinal);
    }

    #endregion
}
