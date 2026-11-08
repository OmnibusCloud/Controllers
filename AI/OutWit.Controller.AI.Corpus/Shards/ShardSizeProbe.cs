using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Asks the origin how big the shards are (HEAD, Content-Length) for the sources whose
/// manifest entry carries no size, so the preflight's traffic line means something to a
/// contributor. Allowlisted origins only, a few requests at a time, a bounded number of
/// probes: a manifest of thousands of shards gets a sample and an extrapolation, not a
/// thousand requests.
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
    /// <param name="client">The HTTP client.</param>
    /// <param name="cancellationToken">Cancels the probes.</param>
    /// <returns>What was learned.</returns>
    public static async Task<ShardSizeProbeResult> ProbeAsync(IReadOnlyList<CorpusSourceData> sources, IReadOnlyCollection<string> allowedOrigins, HttpClient client, CancellationToken cancellationToken = default)
    {
        var unsized = sources.Where(source => source.SizeBytes <= 0).ToList();
        if (unsized.Count == 0)
            return new ShardSizeProbeResult(0, 0, 0, 0);

        var candidates = unsized.Take(MAX_PROBES).ToList();
        var sizes = new long?[candidates.Count];
        using var gate = new SemaphoreSlim(CONCURRENCY, CONCURRENCY);

        await Task.WhenAll(candidates.Select(async (source, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                sizes[index] = await HeadAsync(source, allowedOrigins, client, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));

        var known = sizes.Where(size => size.HasValue).Select(size => size!.Value).ToList();
        var probed = known.Count;
        var failed = candidates.Count - probed;
        var unprobed = unsized.Count - candidates.Count;
        var mean = probed > 0 ? (long)known.Average() : 0;

        return new ShardSizeProbeResult(known.Sum() + mean * (failed + unprobed), probed, failed, unprobed);
    }

    private static async Task<long?> HeadAsync(CorpusSourceData source, IReadOnlyCollection<string> allowedOrigins, HttpClient client, CancellationToken cancellationToken)
    {
        if (ShardOrigin.Check(source.Url, allowedOrigins, out var uri) != null || uri == null)
            return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TIMEOUT);
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
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
