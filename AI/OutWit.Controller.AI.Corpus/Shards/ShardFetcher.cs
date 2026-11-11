using System.Net;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Fetches one shard from its public origin to a local file: streamed, capped in bytes,
/// retried on transient failures (connection errors, 5xx, 429, stalls). A 4xx other than 429
/// is final — the origin said no. The cap is checked against Content-Length before a byte is
/// read and against the running count while reading. A retry after a broken body asks for the
/// rest with a Range request and appends when the origin honours it (206); an origin that
/// answers 200 starts the file over. Redirects are followed one hop at a time, each checked
/// against the task's list and the machine's policy; a refusal there is final.
/// </summary>
public sealed class ShardFetcher
{
    #region Constants

    private const int BUFFER_SIZE = 1 << 20;

    #endregion

    #region Fields

    private readonly HttpClient m_client;
    private readonly ShardFetchPolicy m_policy;
    private readonly ShardOriginPolicy m_origins;
    private readonly SemaphoreSlim? m_gate;

    #endregion

    #region Constructors

    /// <summary>Creates a fetcher for a machine.</summary>
    /// <param name="policy">Retries and stall limit.</param>
    /// <param name="origins">What this machine fetches from.</param>
    public ShardFetcher(ShardFetchPolicy policy, ShardOriginPolicy origins)
    {
        m_client = ShardHttp.CreateClient(origins);
        m_policy = policy;
        m_origins = origins;
        m_gate = policy.MaxConcurrent > 0 ? new SemaphoreSlim(policy.MaxConcurrent, policy.MaxConcurrent) : null;
    }

    #endregion

    #region Functions

    /// <summary>Fetches a shard to a file, retrying per the policy.</summary>
    /// <param name="uri">The shard URL (already checked).</param>
    /// <param name="allowedOrigins">Host names the task allows; a redirect may only lead to one of them.</param>
    /// <param name="destinationPath">The file to write; replaced on every attempt.</param>
    /// <param name="maxBytes">Most bytes accepted (0 = no cap).</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The byte count written.</returns>
    /// <exception cref="ShardOversizedException">The shard exceeds the cap.</exception>
    /// <exception cref="ShardOriginRefusedException">The request would have left what this machine fetches from.</exception>
    /// <exception cref="HttpRequestException">The origin refused or kept failing.</exception>
    public async Task<long> FetchAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        return (await FetchMeasuredAsync(uri, allowedOrigins, destinationPath, maxBytes, cancellationToken)).Bytes;
    }

    /// <summary>Fetches a shard to a file like <see cref="FetchAsync"/> and says how long it waited for a download slot first.</summary>
    /// <param name="uri">The shard URL (already checked).</param>
    /// <param name="allowedOrigins">Host names the task allows; a redirect may only lead to one of them.</param>
    /// <param name="destinationPath">The file to write; replaced on every attempt.</param>
    /// <param name="maxBytes">Most bytes accepted (0 = no cap).</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The byte count written and the wait for the slot.</returns>
    /// <exception cref="ShardOversizedException">The shard exceeds the cap.</exception>
    /// <exception cref="ShardOriginRefusedException">The request would have left what this machine fetches from.</exception>
    /// <exception cref="HttpRequestException">The origin refused or kept failing.</exception>
    public Task<(long Bytes, TimeSpan Waited)> FetchMeasuredAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        return FetchMeasuredAsync(uri, allowedOrigins, destinationPath, maxBytes, null, cancellationToken);
    }

    /// <summary>Fetches a shard to a file like <see cref="FetchAsync"/>, says how long it waited for a download slot first, and tells the caller how much has arrived on the way.</summary>
    /// <param name="uri">The shard URL (already checked).</param>
    /// <param name="allowedOrigins">Host names the task allows; a redirect may only lead to one of them.</param>
    /// <param name="destinationPath">The file to write; replaced on every attempt.</param>
    /// <param name="maxBytes">Most bytes accepted (0 = no cap).</param>
    /// <param name="received">Called with the bytes of the shard on disk so far; an attempt that starts over calls it with a smaller number. Null for none.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The byte count written and the wait for the slot.</returns>
    /// <exception cref="ShardOversizedException">The shard exceeds the cap.</exception>
    /// <exception cref="ShardOriginRefusedException">The request would have left what this machine fetches from.</exception>
    /// <exception cref="HttpRequestException">The origin refused or kept failing.</exception>
    public async Task<(long Bytes, TimeSpan Waited)> FetchMeasuredAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, Action<long>? received, CancellationToken cancellationToken)
    {
        if (m_gate == null)
            return (await FetchWithRetriesAsync(uri, allowedOrigins, destinationPath, maxBytes, received, cancellationToken), TimeSpan.Zero);

        // Several shards may run on one node at once; the origin sees at most the policy's
        // number of them downloading, the rest wait their turn (fetch is a small share of a
        // shard's time, so the wait costs little throughput).
        var queued = System.Diagnostics.Stopwatch.StartNew();
        await m_gate.WaitAsync(cancellationToken);
        var waited = queued.Elapsed;
        try
        {
            return (await FetchWithRetriesAsync(uri, allowedOrigins, destinationPath, maxBytes, received, cancellationToken), waited);
        }
        finally
        {
            m_gate.Release();
        }
    }

    private async Task<long> FetchWithRetriesAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, Action<long>? received, CancellationToken cancellationToken)
    {
        Exception? last = null;
        var resume = false;

        for (var attempt = 1; attempt <= m_policy.Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await FetchOnceAsync(uri, allowedOrigins, destinationPath, maxBytes, resume, received, cancellationToken);
            }
            catch (ShardOversizedException)
            {
                throw;
            }
            catch (Exception exception) when (ShardOriginRefusedException.Find(exception) is { } refusal)
            {
                // The HTTP stack wraps what the address rule throws while connecting; a retry
                // cannot change the answer, so the refusal itself goes up.
                if (ReferenceEquals(refusal, exception))
                    throw;

                throw refusal;
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

    private async Task<long> FetchOnceAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, bool resume, Action<long>? received, CancellationToken cancellationToken)
    {
        var have = resume && File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;

        using var response = await ShardHttp.SendAsync(m_client, HttpMethod.Get, uri, have, allowedOrigins, m_origins, cancellationToken);

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

        // Where this attempt starts from: the bytes kept from the previous one, or nothing.
        received?.Invoke(total);

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
            received?.Invoke(total);
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

    #region Properties

    /// <summary>What this machine fetches from.</summary>
    public ShardOriginPolicy Origins => m_origins;

    #endregion
}
