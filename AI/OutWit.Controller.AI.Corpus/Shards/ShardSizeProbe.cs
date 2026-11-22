using System.Security.Cryptography;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Asks the origin how big the shards are (HEAD, Content-Length), for a sample drawn at random:
/// <list type="bullet">
/// <item>of the sources whose manifest entry carries no size, so that the estimates mean
/// something and the answered ones get a size the nodes hold them to;</item>
/// <item>of the sources that do carry a size, so that a manifest whose sizes are far below the
/// origin's is found out before the job starts (the nodes refuse such a shard either way).</item>
/// </list>
/// The sample is drawn by the server's own random source, so whoever writes a manifest cannot
/// know which entries will be asked; half the probe budget goes to each kind when both are there.
/// The requests leave the machine that plans the job, so they obey that machine's policy like any
/// fetch: a source this machine does not fetch from is not asked. A few requests at a time, a
/// bounded number in all: a manifest of thousands of shards gets a sample and an extrapolation,
/// not a thousand requests.
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

    /// <summary>Probes a random sample of the sources and says what every shard is counted at and held to.</summary>
    /// <param name="sources">The manifest's sources.</param>
    /// <param name="allowedOrigins">The allowlist; a source off it is not probed.</param>
    /// <param name="policy">What this machine fetches from; a source outside it is not probed.</param>
    /// <param name="client">A client from <see cref="ShardHttp.CreateClient"/> for the same policy.</param>
    /// <param name="cancellationToken">Cancels the probes.</param>
    /// <param name="random">Draws a number from 0 up to (not including) its argument; null for the platform's cryptographic source. A test gives its own.</param>
    /// <returns>What was learned.</returns>
    public static async Task<ShardSizeProbeResult> ProbeAsync(IReadOnlyList<CorpusSourceData> sources, IReadOnlyCollection<string> allowedOrigins, ShardOriginPolicy policy, HttpClient client, CancellationToken cancellationToken = default, Func<int, int>? random = null)
    {
        random ??= RandomNumberGenerator.GetInt32;

        var unsized = new List<int>();
        var sized = new List<int>();
        var unsizedTotal = 0;

        for (var index = 0; index < sources.Count; index++)
        {
            var isUnsized = sources[index].SizeBytes <= 0;
            if (isUnsized)
                unsizedTotal++;

            if (MayProbe(sources[index], allowedOrigins, policy))
                (isUnsized ? unsized : sized).Add(index);
        }

        // Half the budget each; what one kind does not use goes to the other.
        var takeUnsized = Math.Min(unsized.Count, MAX_PROBES / 2 + Math.Max(0, MAX_PROBES / 2 - sized.Count));
        var takeSized = Math.Min(sized.Count, MAX_PROBES - takeUnsized);
        var asked = Draw(unsized, takeUnsized, random).Concat(Draw(sized, takeSized, random)).ToList();

        var answers = new Dictionary<int, long?>(asked.Count);
        using (var gate = new SemaphoreSlim(CONCURRENCY, CONCURRENCY))
        {
            var pending = asked.Select(async index =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    return (Index: index, Size: await HeadAsync(sources[index], allowedOrigins, policy, client, cancellationToken));
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            foreach (var answer in await Task.WhenAll(pending))
                answers[answer.Index] = answer.Size;
        }

        var sizes = new long[sources.Count];
        var declared = new long[sources.Count];
        var over = new List<ShardSizeMismatch>();
        long probedBytes = 0;
        int probed = 0, failed = 0, verified = 0;

        for (var index = 0; index < sources.Count; index++)
        {
            var stated = sources[index].SizeBytes;
            var answered = answers.TryGetValue(index, out var answer);

            if (stated > 0)
            {
                sizes[index] = declared[index] = stated;
                if (answer is not { } atOrigin)
                    continue;

                verified++;
                if (atOrigin > ShardLimits.DeclaredCap(stated))
                    over.Add(new ShardSizeMismatch(index, stated, atOrigin));
            }
            else if (answer is > 0)
            {
                // The origin's own word: the shard is counted at it and held to it.
                sizes[index] = declared[index] = answer.Value;
                probedBytes += answer.Value;
                probed++;
            }
            else if (answered)
                failed++;
        }

        // An unsized source nobody answered for is counted at the mean of the answers: they are a
        // random sample of the unsized sources this machine may ask. It stays without a size of
        // its own, and a node holds it to the cap.
        var mean = probed > 0 ? probedBytes / probed : 0;
        var withoutSize = 0;
        for (var index = 0; index < sources.Count; index++)
        {
            if (declared[index] > 0)
                continue;

            withoutSize++;
            sizes[index] = mean;
        }

        return new ShardSizeProbeResult(probedBytes + mean * withoutSize, probed, failed, unsizedTotal - probed - failed)
        {
            Sizes = sizes,
            Declared = declared,
            Unsized = withoutSize,
            Verified = verified,
            Overdeclared = over,
            MeanBytes = mean
        };
    }

    /// <summary>What a job knows of its shards' sizes when nothing is asked at the origin: the manifest's own, each a claim the nodes hold it to.</summary>
    /// <param name="sources">The manifest's sources.</param>
    /// <returns>The sizes; a source without one has none.</returns>
    public static ShardSizeProbeResult Unasked(IReadOnlyList<CorpusSourceData> sources)
    {
        var sizes = sources.Select(source => Math.Max(0, source.SizeBytes)).ToArray();
        var unsized = sizes.Count(size => size == 0);

        return new ShardSizeProbeResult(0, 0, 0, unsized) { Sizes = sizes, Declared = sizes, Unsized = unsized };
    }

    #endregion

    #region Tools

    /// <summary>Draws <paramref name="count"/> of the candidates at random, each as likely as any other, and puts them in manifest order.</summary>
    private static List<int> Draw(List<int> candidates, int count, Func<int, int> random)
    {
        for (var index = 0; index < count; index++)
        {
            var pick = index + random(candidates.Count - index);
            (candidates[index], candidates[pick]) = (candidates[pick], candidates[index]);
        }

        var drawn = candidates.GetRange(0, count);
        drawn.Sort();
        return drawn;
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
