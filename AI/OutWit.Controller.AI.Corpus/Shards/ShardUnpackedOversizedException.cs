namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The shard unpacks to more than a shard may: a few hundred kilobytes of gzip can hold hundreds of
/// megabytes of nothing. The reader stops at the ceiling and the shard is <c>Oversized</c>, the
/// same on every node; nothing of it is kept.
/// </summary>
public sealed class ShardUnpackedOversizedException(long limit) : Exception($"The shard unpacks to more than {limit} bytes.")
{
    #region Properties

    /// <summary>The ceiling that was exceeded.</summary>
    public long Limit { get; } = limit;

    #endregion
}
