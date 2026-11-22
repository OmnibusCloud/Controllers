namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>The shard grew past the task's byte cap while being fetched; nothing of it is kept.</summary>
public sealed class ShardOversizedException(long limit) : Exception($"The shard exceeds the task's cap of {limit} bytes.")
{
    #region Properties

    /// <summary>The cap that was exceeded.</summary>
    public long Limit { get; } = limit;

    #endregion
}
