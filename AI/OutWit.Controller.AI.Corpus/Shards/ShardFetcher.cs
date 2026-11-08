using System.Net;
using System.Net.Http.Headers;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Fetches one shard from its public origin to a local file: streamed, capped in bytes,
/// retried on transient failures (connection errors, 5xx, 429, stalls). A 4xx other than 429
/// is final — the origin said no. The cap is checked against Content-Length before a byte is
/// read and against the running count while reading.
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

    #endregion

    #region Constructors

    /// <summary>Creates a fetcher over an HTTP client.</summary>
    /// <param name="client">The client; its timeout should be infinite — the policy's stall limit bounds the wait.</param>
    /// <param name="policy">Retries and stall limit.</param>
    public ShardFetcher(HttpClient client, ShardFetchPolicy policy)
    {
        m_client = client;
        m_policy = policy;
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
        Exception? last = null;

        for (var attempt = 1; attempt <= m_policy.Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await FetchOnceAsync(uri, destinationPath, maxBytes, cancellationToken);
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

    private async Task<long> FetchOnceAsync(Uri uri, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await m_client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"'{uri}' answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);

        if (maxBytes > 0 && response.Content.Headers.ContentLength is { } length && length > maxBytes)
            throw new ShardOversizedException(maxBytes);

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BUFFER_SIZE, FileOptions.Asynchronous);

        var buffer = new byte[BUFFER_SIZE];
        long total = 0;

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
