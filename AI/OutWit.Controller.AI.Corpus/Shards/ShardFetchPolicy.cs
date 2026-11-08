namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// How hard a node tries: attempts, the back-off between them (doubling, with jitter so a
/// fleet does not hammer an origin in lockstep) and how long a read may stall before the
/// attempt is abandoned.
/// </summary>
/// <param name="Attempts">Fetch attempts before the task fails (1 = no retry).</param>
/// <param name="BaseDelay">Delay before the second attempt; each later one doubles it.</param>
/// <param name="ReadStall">Longest wait for the next chunk of the body.</param>
/// <param name="MaxConcurrent">Most fetches in flight at once through one fetcher — the node's politeness towards the origin (0 = no ceiling).</param>
public sealed record ShardFetchPolicy(int Attempts, TimeSpan BaseDelay, TimeSpan ReadStall, int MaxConcurrent = 0)
{
    #region Constants

    /// <summary>Three attempts, 2 s then 4 s between them, a 60 s stall limit, two fetches at once per node.</summary>
    public static readonly ShardFetchPolicy Default = new(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), 2);

    /// <summary>Three attempts with no waiting and no ceiling: the tests' policy.</summary>
    public static readonly ShardFetchPolicy Immediate = new(3, TimeSpan.Zero, TimeSpan.FromSeconds(60));

    #endregion
}
