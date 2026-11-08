namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// What processing one shard produced: the identity of the uncompressed product and the
/// counters (bytes and times are filled by the caller, who owns the files and the clock).
/// </summary>
/// <param name="ContentSha256">Lower-case hex SHA-256 of the uncompressed JSON lines.</param>
/// <param name="Stats">The counters.</param>
/// <param name="SkippedOversized">Records the reader skipped for exceeding its record cap.</param>
/// <param name="Truncated">Whether the shard ended mid-record.</param>
public sealed record ShardProduct(string ContentSha256, ShardStatsBuilder Stats, int SkippedOversized, bool Truncated);
