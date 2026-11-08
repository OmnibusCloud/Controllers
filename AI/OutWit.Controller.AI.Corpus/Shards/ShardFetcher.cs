using System.Net;
using System.Net.Http.Headers;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Fetches one shard from its public origin to a local file: streamed, capped in bytes,
/// retried on transient failures (connection errors, 5xx, 429, stalls). A 4xx other than 429
/// is final — the origin said no. The cap is checked against Content-Length before a byte is
/// read and against the running count while reading. A retry after a broken body asks for the
/// rest with a Range request and appends when the origin honours it (206); an origin that
/// answers 200 starts the file over.
/// </summary>
public sealed class ShardFetcher
{
    #region Constants

    private const int BUFFER_SIZE = 1 << 20;

    /// <summary>What the origin sees: the controller's name and version.</summary>
    public static readonly string USER_AGENT = $"OutWit-Corpus/{ControllerBuildInfo.VERSION}";

    #endregion

    #region Fields

    private readonly HttpClient m_client;
    private readonly ShardFetchPolicy m_policy;
    private readonly SemaphoreSlim? m_gate;

    #endregion

    #region Constructors

    /// <summary>Creates a fetcher over an HTTP client.</summary>
    /// <param name="client">The client; its timeout should be infinite — the policy's stall limit bounds the wait.</param>
    /// <param name="policy">Retries and stall limit.</param>
    public ShardFetcher(HttpClient client, ShardFetchPolicy policy)
    {
        m_client = client;
        m_policy = policy;
        m_gate = policy.MaxConcurrent > 0 ? new SemaphoreSlim(policy.MaxConcurrent, policy.MaxConcurrent) : null;
    }

    #endregion

    #region Functions

    /// <summary>Fetches a shard to a file, retrying per the policy.</summary>
    /// <param name="uri">The shard URL (already allowlisted).</param>
    /// <param name="destinationPath">The file to write; replaced on every attempt.</param>
    /// <param name="maxBytes">Most bytes accepted (0 = no cap).</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The byte count written.</returns>
    /// <exception cref="ShardOversizedException">The shard exceeds the cap.</exception>
    /// <exception cref="HttpRequestException">The origin refused or kept failing.</exception>
    public async Task<long> FetchAsync(Uri uri, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        return (await FetchMeasuredAsync(uri, destinationPath, maxBytes, cancellationToken)).Bytes;
    }

    /// <summary>Fetches a shard to a file like <see cref="FetchAsync"/> and says how long it waited for a download slot first.</summary>
    /// <param name="uri">The shard URL (already allowlisted).</param>
    /// <param name="destinationPath">The file to write; replaced on every attempt.</param>
    /// <param name="maxBytes">Most bytes accepted (0 = no cap).</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The byte count written and the wait for the slot.</returns>
    /// <exception cref="ShardOversizedException">The shard exceeds the cap.</exception>
    /// <exception cref="HttpRequestException">The origin refused or kept failing.</exception>
    public async Task<(long Bytes, TimeSpan Waited)> FetchMeasuredAsync(Uri uri, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        if (m_gate == null)
            return (await FetchWithRetriesAsync(uri, destinationPath, maxBytes, cancellationToken), TimeSpan.Zero);

        // Several shards may run on one node at once; the origin sees at most the policy's
        // number of them downloading, the rest wait their turn (fetch is a small share of a
        // shard's time, so the wait costs little throughput).
        var queued = System.Diagnostics.Stopwatch.StartNew();
        await m_gate.WaitAsync(cancellationToken);
        var waited = queued.Elapsed;
        try
        {
            return (await FetchWithRetriesAsync(uri, destinationPath, maxBytes, cancellationToken), waited);
        }
        finally
        {
            m_gate.Release();
        }
    }

    private async Task<long> FetchWithRetriesAsync(Uri uri, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        Exception? last = null;
        var resume = false;

        for (var attempt = 1; attempt <= m_policy.Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await FetchOnceAsync(uri, destinationPath, maxBytes, resume, cancellationToken);
            }
            catch (ShardOversizedException)
            {
                throw;
            }
            catch (HttpRequestException exception) when (!IsTransient(exception.StatusCode))
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
            {
                last = exception;
                // Something arrived before the failure: ask for the rest next time.
                resume = exception is not HttpRequestException { StatusCode: not null } && File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0;
            }

            if (attempt < m_policy.Attempts)
                await Task.Delay(Backoff(attempt), cancellationToken);
        }

        throw new HttpRequestException($"Fetching '{uri}' failed after {m_policy.Attempts} attempt(s): {last?.Message}", last);
    }

    /// <summary>Creates the client a node uses: no client-side timeout (the policy bounds stalls), automatic decompression off (shards are gzip and stay gzip).</summary>
    /// <returns>The client.</returns>
    public static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(30)
        };

        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse(USER_AGENT));
        return client;
    }

    private async Task<long> FetchOnceAsync(Uri uri, string destinationPath, long maxBytes, bool resume, CancellationToken cancellationToken)
    {
        var have = resume && File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (have > 0)
            request.Headers.Range = new RangeHeaderValue(have, null);

        using var response = await m_client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The partial file is not something the origin can continue; start over.
            File.Delete(destinationPath);
            throw new IOException($"'{uri}' could not continue from byte {have}.");
        }

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"'{uri}' answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);

        var appending = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var expected = appending
            ? response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength is { } rest ? have + rest : null)
            : response.Content.Headers.ContentLength;
        if (maxBytes > 0 && expected is { } length && length > maxBytes)
            throw new ShardOversizedException(maxBytes);

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = new FileStream(destinationPath, appending ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BUFFER_SIZE, FileOptions.Asynchronous);

        var buffer = new byte[BUFFER_SIZE];
        var total = appending ? have : 0;

        while (true)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(m_policy.ReadStall);

            int read;
            try
            {
                read = await body.ReadAsync(buffer, stall.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException($"'{uri}' stalled for more than {m_policy.ReadStall.TotalSeconds:F0} s.");
            }

            if (read == 0)
                break;

            total += read;
            if (maxBytes > 0 && total > maxBytes)
                throw new ShardOversizedException(maxBytes);

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return total;
    }

    #endregion

    #region Tools

    private static bool IsTransient(HttpStatusCode? status)
    {
        // No status: the connection failed. 429 and 5xx: the origin may recover.
        return status == null || status == HttpStatusCode.TooManyRequests || (int)status >= 500;
    }

    private TimeSpan Backoff(int attempt)
    {
        if (m_policy.BaseDelay == TimeSpan.Zero)
            return TimeSpan.Zero;

        var delay = m_policy.BaseDelay * Math.Pow(2, attempt - 1);
        var jitter = delay * (Random.Shared.NextDouble() * 0.5);
        return delay + jitter;
    }

    #endregion
}
