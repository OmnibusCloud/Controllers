namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The shard's product passes what a shard of its fetched size may give
/// (<see cref="ShardProductLimits"/>): more documents, or more compressed text. The processor
/// stops at the document that passes the bound and the shard is <c>Oversized</c>
/// (<c>oversized_product</c>), the same on every node; nothing of it is kept.
/// </summary>
public sealed class ShardProductOversizedException(string what, long limit) : Exception($"The shard's product has more {what} than {limit}.")
{
    #region Properties

    /// <summary>What passed its bound: documents, or bytes of compressed text.</summary>
    public string What { get; } = what;

    /// <summary>The bound that was passed.</summary>
    public long Limit { get; } = limit;

    #endregion
}
