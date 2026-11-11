using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Asks the origin how big the shards are (HEAD, Content-Length) for the sources whose
/// manifest entry carries no size, so the preflight's traffic line means something to a
/// contributor. The requests leave the machine that plans the job, so they obey that
/// machine's policy like any fetch: a source this machine does not fetch from is not asked.
/// A few requests at a time, a bounded number of probes: a manifest of thousands of shards
/// gets a sample and an extrapolation, not a thousand requests.
/// </summary>
public static class ShardSizeProbe
{
    #region Constants

    /// <summary>Sources probed at most per preflight.</summary>
    public const int MAX_PROBES = 64;

    /// <summary>Probes in flight at once.</summary>
    public const int CONCURRENCY = 4;

    /// <summary>Longest wait for one answer.</summary>
    public static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(15);

    #endregion

    #region Functions

    /// <summary>Probes the sizes of the sources that have none.</summary>
    /// <param name="sources">The manifest's sources.</param>
    /// <param name="allowedOrigins">The allowlist; a source off it is not probed.</param>
    /// <param name="policy">What this machine fetches from; a source outside it is not probed.</param>
    /// <param name="client">A client from <see cref="ShardHttp.CreateClient"/> for the same policy.</param>
    /// <param name="cancellationToken">Cancels the probes.</param>
    /// <returns>What was learned.</returns>
    public static async Task<ShardSizeProbeResult> ProbeAsync(IReadOnlyList<CorpusSourceData> sources, IReadOnlyCollection<string> allowedOrigins, ShardOriginPolicy policy, HttpClient client, CancellationToken cancellationToken = default)
    {
        var sizes = await ProbeSizesAsync(sources, allowedOrigins, policy, client, cancellationToken);

        var unsized = sources.Count(source => source.SizeBytes <= 0);
        if (unsized == 0)
            return new ShardSizeProbeResult(0, 0, 0, 0);

        var known = sizes.Where(size => size.HasValue).Select(size => size!.Value).ToList();
        var probed = known.Count;
        var attempted = Math.Min(sources.Count(source => source.SizeBytes <= 0 && MayProbe(source, allowedOrigins, policy)), MAX_PROBES);
        var failed = attempted - probed;
        var unprobed = unsized - attempted;
        var mean = probed > 0 ? (long)known.Average() : 0;

        return new ShardSizeProbeResult(known.Sum() + mean * (failed + unprobed), probed, failed, unprobed);
    }

    /// <summary>The origin's size of every unsized source, aligned with the sources (null = sized already, not probed, or unanswered).</summary>
    /// <param name="sources">The manifest's sources.</param>
    /// <param name="allowedOrigins">The allowlist; a source off it is not probed.</param>
    /// <param name="policy">What this machine fetches from; a source outside it is not probed.</param>
    /// <param name="client">A client from <see cref="ShardHttp.CreateClient"/> for the same policy.</param>
    /// <param name="cancellationToken">Cancels the probes.</param>
    /// <returns>One entry per source.</returns>
    public static async Task<long?[]> ProbeSizesAsync(IReadOnlyList<CorpusSourceData> sources, IReadOnlyCollection<string> allowedOrigins, ShardOriginPolicy policy, HttpClient client, CancellationToken cancellationToken = default)
    {
        var sizes = new long?[sources.Count];
        var candidates = sources.Select((source, index) => (source, index))
            .Where(entry => entry.source.SizeBytes <= 0 && MayProbe(entry.source, allowedOrigins, policy))
            .Take(MAX_PROBES).ToList();
        using var gate = new SemaphoreSlim(CONCURRENCY, CONCURRENCY);

        await Task.WhenAll(candidates.Select(async entry =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                sizes[entry.index] = await HeadAsync(entry.source, allowedOrigins, policy, client, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));

        return sizes;
    }

    private static bool MayProbe(CorpusSourceData source, IReadOnlyCollection<string> allowedOrigins, ShardOriginPolicy policy)
    {
        return ShardOrigin.Check(source.Url, allowedOrigins, policy, out var uri) == null && uri != null;
    }

    private static async Task<long?> HeadAsync(CorpusSourceData source, IReadOnlyCollection<string> allowedOrigins, ShardOriginPolicy policy, HttpClient client, CancellationToken cancellationToken)
    {
        if (ShardOrigin.Check(source.Url, allowedOrigins, policy, out var uri) != null || uri == null)
            return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TIMEOUT);
            using var response = await ShardHttp.SendAsync(client, HttpMethod.Head, uri, 0, allowedOrigins, policy, timeout.Token);
            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or ShardOriginRefusedException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    #endregion
}

/// <summary>What the probe learned.</summary>
/// <param name="EstimatedBytes">Bytes of the unsized sources: the probed sizes plus the mean for the rest.</param>
/// <param name="Probed">Sources whose size the origin gave.</param>
/// <param name="Failed">Sources probed without an answer.</param>
/// <param name="Unprobed">Unsized sources beyond the probe budget, estimated at the mean.</param>
public sealed record ShardSizeProbeResult(long EstimatedBytes, int Probed, int Failed, int Unprobed);
