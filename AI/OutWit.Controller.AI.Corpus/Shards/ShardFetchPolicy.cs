namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// How hard a node tries: attempts, the back-off between them (doubling, with jitter so a
/// fleet does not hammer an origin in lockstep), how long a read may stall before the attempt is
/// abandoned, how patient it is with an origin that asks it to slow down (429, 503), and the
/// slowest a body may arrive.
/// </summary>
/// <param name="Attempts">Fetch attempts that failed for another reason than throttling before the task gives up (1 = no retry).</param>
/// <param name="BaseDelay">Delay before the second attempt; each later one doubles it.</param>
/// <param name="ReadStall">Longest wait for the next chunk of the body.</param>
/// <param name="MaxConcurrent">Most fetches in flight at once through one fetcher — the node's politeness towards the origin (0 = no ceiling).</param>
/// <param name="ThrottleAttempts">Answers of 429 or 503 the origin may give before the task gives up; they do not count against <paramref name="Attempts"/>.</param>
/// <param name="ThrottleDelay">Delay after the first throttled answer; each later one doubles it. An answer's <c>Retry-After</c> is honoured instead when it is longer.</param>
/// <param name="MaxDelay">The longest any one delay may be, <c>Retry-After</c> included (zero = no ceiling).</param>
/// <param name="MinBytesPerSecond">The slowest a body may arrive over a whole attempt (0 = no floor): an origin that trickles a byte a minute never stalls and would hold a download slot for ever.</param>
/// <param name="Slack">The time an attempt has beyond what its bytes take at the floor rate.</param>
public sealed record ShardFetchPolicy(
    int Attempts,
    TimeSpan BaseDelay,
    TimeSpan ReadStall,
    int MaxConcurrent = 0,
    int ThrottleAttempts = 1,
    TimeSpan ThrottleDelay = default,
    TimeSpan MaxDelay = default,
    long MinBytesPerSecond = 0,
    TimeSpan Slack = default)
{
    #region Constants

    /// <summary>
    /// Three attempts, 2 s then 4 s between them, a 60 s stall limit, two fetches at once per
    /// node; six throttled answers with 5, 10, 20, 40 and 80 s between them (or the origin's
    /// <c>Retry-After</c>, two minutes at most each); a body no slower than 32 KiB/s over an
    /// attempt, with five minutes of slack.
    /// </summary>
    public static readonly ShardFetchPolicy Default = new(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), 2,
        ThrottleAttempts: 6, ThrottleDelay: TimeSpan.FromSeconds(5), MaxDelay: TimeSpan.FromMinutes(2),
        MinBytesPerSecond: 32 * 1024, Slack: TimeSpan.FromMinutes(5));

    /// <summary>Three attempts with no waiting and no ceiling: the tests' policy.</summary>
    public static readonly ShardFetchPolicy Immediate = new(3, TimeSpan.Zero, TimeSpan.FromSeconds(60), ThrottleAttempts: 3);

    #endregion
}
