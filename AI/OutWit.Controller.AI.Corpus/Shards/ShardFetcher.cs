using System.Net;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Fetches one shard from its public origin to a local file: streamed, capped in bytes, retried
/// on transient failures (connection errors, 5xx, stalls), and patient with an origin that asks a
/// fleet to slow down (429 and 503: more attempts, longer waits, the origin's <c>Retry-After</c>
/// honoured within a ceiling). A 4xx other than 429 is final — the origin said no. The cap is
/// checked against Content-Length before a byte is read and against the running count while
/// reading. A retry after a broken body asks for the rest with a Range request and appends when
/// the origin honours it (206); an origin that answers 200 starts the file over. Redirects are
/// followed one hop at a time, each checked against the task's list and the machine's policy; a
/// refusal there is final. Every hop has the stall limit to send its answer's head, so an origin
/// that takes the request and never answers is a broken answer like a body that stalls. A body
/// must keep a floor rate over the whole attempt, so an origin that trickles cannot hold a
/// download slot for ever.
/// <para>
/// Three ways it can fail, kept apart because they mean different things to a run: the origin
/// answered with a status that is not a success (<see cref="ShardFetchException.Status"/>; the
/// caller decides whether it is a statement about the shard or may be this node's own trouble:
/// <see cref="ShardFetchException.MayBeTheNodes"/>), nobody answered or the answer kept breaking
/// (<see cref="ShardFetchException"/> without a status: possibly this node's own link), and a file
/// of the node's own could not be written (<see cref="ShardLocalFaultException"/>: this node's
/// disk).
/// </para>
/// </summary>
public sealed class ShardFetcher
{
    #region Constants

    private const int BUFFER_SIZE = 1 << 20;

    /// <summary>The size an attempt's deadline is sized for when neither the origin nor the cap gives one.</summary>
    private const long UNKNOWN_SIZE_BYTES = 1L << 30;

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
    /// <exception cref="ShardFetchException">The origin refused or kept failing.</exception>
    /// <exception cref="ShardLocalFaultException">The file could not be written.</exception>
    public async Task<long> FetchAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, CancellationToken cancellationToken)
    {
        return (await FetchShardAsync(uri, allowedOrigins, destinationPath, maxBytes, null, cancellationToken)).Bytes;
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
    /// <exception cref="ShardFetchException">The origin refused or kept failing.</exception>
    /// <exception cref="ShardLocalFaultException">The file could not be written.</exception>
    public async Task<(long Bytes, TimeSpan Waited)> FetchMeasuredAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, Action<long>? received, CancellationToken cancellationToken)
    {
        var outcome = await FetchShardAsync(uri, allowedOrigins, destinationPath, maxBytes, received, cancellationToken);
        return (outcome.Bytes, outcome.Waited);
    }

    /// <summary>Fetches a shard to a file and says what arrived: the bytes, the wait for a download slot, and whether the origin framed the body.</summary>
    /// <param name="uri">The shard URL (already checked).</param>
    /// <param name="allowedOrigins">Host names the task allows; a redirect may only lead to one of them.</param>
    /// <param name="destinationPath">The file to write; replaced on every attempt.</param>
    /// <param name="maxBytes">Most bytes accepted (0 = no cap).</param>
    /// <param name="received">Called with the bytes of the shard on disk so far; an attempt that starts over calls it with a smaller number. Null for none.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>What arrived.</returns>
    /// <exception cref="ShardOversizedException">The shard exceeds the cap.</exception>
    /// <exception cref="ShardOriginRefusedException">The request would have left what this machine fetches from.</exception>
    /// <exception cref="ShardFetchException">The origin refused or kept failing.</exception>
    /// <exception cref="ShardLocalFaultException">The file could not be written.</exception>
    public async Task<ShardFetchOutcome> FetchShardAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, Action<long>? received, CancellationToken cancellationToken)
    {
        if (m_gate == null)
        {
            var (bytes, framed) = await FetchWithRetriesAsync(uri, allowedOrigins, destinationPath, maxBytes, received, cancellationToken);
            return new ShardFetchOutcome(bytes, TimeSpan.Zero, framed);
        }

        // Several shards may run on one node at once; the origin sees at most the policy's
        // number of them downloading, the rest wait their turn (fetch is a small share of a
        // shard's time, so the wait costs little throughput).
        var queued = System.Diagnostics.Stopwatch.StartNew();
        await m_gate.WaitAsync(cancellationToken);
        var waited = queued.Elapsed;
        try
        {
            var (bytes, framed) = await FetchWithRetriesAsync(uri, allowedOrigins, destinationPath, maxBytes, received, cancellationToken);
            return new ShardFetchOutcome(bytes, waited, framed);
        }
        finally
        {
            m_gate.Release();
        }
    }

    private async Task<(long Bytes, bool Framed)> FetchWithRetriesAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, Action<long>? received, CancellationToken cancellationToken)
    {
        var resume = false;
        var failures = 0;
        var throttled = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan delay;
            try
            {
                return await FetchOnceAsync(uri, allowedOrigins, destinationPath, maxBytes, resume, received, cancellationToken);
            }
            catch (ShardOversizedException)
            {
                throw;
            }
            catch (ShardLocalFaultException)
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (StatusException exception)
            {
                // The origin answered. A 4xx other than 429 is its last word; 429 and 503 ask
                // for patience; another 5xx may pass.
                if (!IsTransient(exception.Status))
                    throw new ShardFetchException(exception.Status);

                if (IsThrottle(exception.Status))
                {
                    if (++throttled >= Math.Max(1, m_policy.ThrottleAttempts))
                        throw new ShardFetchException(exception.Status);

                    delay = ThrottleDelay(throttled, exception.RetryAfter);
                }
                else
                {
                    if (++failures >= Math.Max(1, m_policy.Attempts))
                        throw new ShardFetchException(exception.Status);

                    delay = Backoff(failures);
                }

                resume = false;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
            {
                // Nobody answered, or the body broke: the origin's trouble or this node's link.
                if (++failures >= Math.Max(1, m_policy.Attempts))
                    throw new ShardFetchException(failures, exception);

                delay = Backoff(failures);

                // Something arrived before the failure: ask for the rest next time.
                resume = Local(() => File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0);
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
        }
    }

    private async Task<(long Bytes, bool Framed)> FetchOnceAsync(Uri uri, IReadOnlyCollection<string> allowedOrigins, string destinationPath, long maxBytes, bool resume, Action<long>? received, CancellationToken cancellationToken)
    {
        var have = resume ? Local(() => File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0) : 0;

        // The stall limit holds before the answer's head as after it, on every hop of a redirect:
        // an origin that takes the request and never answers is a broken answer, not a wait.
        using var response = await ShardHttp.SendAsync(m_client, HttpMethod.Get, uri, have, allowedOrigins, m_origins, cancellationToken, m_policy.ReadStall);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The partial file is not something the origin can continue; start over.
            Local(() => File.Delete(destinationPath));
            throw new IOException("The origin could not continue a partial body.");
        }

        if (!response.IsSuccessStatusCode)
            throw new StatusException(response.StatusCode, RetryAfter(response));

        var appending = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        var expected = appending
            ? response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength is { } rest ? have + rest : null)
            : response.Content.Headers.ContentLength;
        if (maxBytes > 0 && expected is { } length && length > maxBytes)
            throw new ShardOversizedException(maxBytes);

        // A body with a length, in chunks or on an HTTP/2 stream cannot end early unnoticed: the
        // HTTP stack fails the read. One that ends when the connection closes can.
        var framed = expected != null || response.Headers.TransferEncodingChunked == true || response.Version.Major >= 2;

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (m_policy.MinBytesPerSecond > 0)
            attempt.CancelAfter(Deadline(expected is { } known ? known - have : maxBytes > 0 ? maxBytes : UNKNOWN_SIZE_BYTES));

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var file = Local(() => new FileStream(destinationPath, appending ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BUFFER_SIZE, FileOptions.Asynchronous));
        var total = appending ? have : 0;
        try
        {
            var buffer = new byte[BUFFER_SIZE];

            // Where this attempt starts from: the bytes kept from the previous one, or nothing.
            received?.Invoke(total);

            while (true)
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token);
                stall.CancelAfter(m_policy.ReadStall);

                int read;
                try
                {
                    read = await body.ReadAsync(buffer, stall.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new IOException("The body stalled or arrived too slowly.");
                }

                if (read == 0)
                    break;

                total += read;
                if (maxBytes > 0 && total > maxBytes)
                    throw new ShardOversizedException(maxBytes);

                try
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                catch (Exception exception) when (ShardLocalFaultException.IsLocal(exception))
                {
                    throw new ShardLocalFaultException(CorpusResultCodes.FAULT_DISK, exception);
                }

                received?.Invoke(total);
            }

            try
            {
                await file.FlushAsync(cancellationToken);
            }
            catch (Exception exception) when (ShardLocalFaultException.IsLocal(exception))
            {
                throw new ShardLocalFaultException(CorpusResultCodes.FAULT_DISK, exception);
            }
        }
        finally
        {
            try
            {
                await file.DisposeAsync();
            }
            catch (Exception exception) when (ShardLocalFaultException.IsLocal(exception))
            {
                // Flushed above when the body was complete; a failed close after another failure adds nothing.
            }
        }

        if (expected is { } whole && total != whole)
            throw new IOException("The body ended before its length.");

        return (total, framed);
    }

    #endregion

    #region Tools

    /// <summary>Runs a step on the node's own files; what the file system refuses is a fault of the node.</summary>
    private static T Local<T>(Func<T> step)
    {
        try
        {
            return step();
        }
        catch (Exception exception) when (ShardLocalFaultException.IsLocal(exception))
        {
            throw new ShardLocalFaultException(CorpusResultCodes.FAULT_DISK, exception);
        }
    }

    private static void Local(Action step)
    {
        Local(() =>
        {
            step();
            return true;
        });
    }

    private static bool IsTransient(HttpStatusCode status)
    {
        // 429 and 5xx: the origin may recover.
        return status == HttpStatusCode.TooManyRequests || (int)status >= 500;
    }

    private static bool IsThrottle(HttpStatusCode status)
    {
        return status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header == null)
            return null;

        if (header.Delta is { } delta)
            return delta;

        return header.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    private TimeSpan Backoff(int attempt)
    {
        if (m_policy.BaseDelay == TimeSpan.Zero)
            return TimeSpan.Zero;

        return Bounded(Jittered(m_policy.BaseDelay * Math.Pow(2, attempt - 1)));
    }

    private TimeSpan ThrottleDelay(int attempt, TimeSpan? retryAfter)
    {
        var delay = m_policy.ThrottleDelay == TimeSpan.Zero ? TimeSpan.Zero : Jittered(m_policy.ThrottleDelay * Math.Pow(2, attempt - 1));

        // The origin's own word, when it asks for longer; never past the ceiling.
        if (retryAfter is { } asked && asked > delay && m_policy.MaxDelay > TimeSpan.Zero)
            delay = asked;

        return Bounded(delay);
    }

    private TimeSpan Bounded(TimeSpan delay)
    {
        return m_policy.MaxDelay > TimeSpan.Zero && delay > m_policy.MaxDelay ? m_policy.MaxDelay : delay;
    }

    private static TimeSpan Jittered(TimeSpan delay)
    {
        return delay + delay * (Random.Shared.NextDouble() * 0.5);
    }

    /// <summary>The longest an attempt may take for the bytes it is to bring: the slack and the bytes at the floor rate.</summary>
    private TimeSpan Deadline(long bytes)
    {
        return m_policy.Slack + TimeSpan.FromSeconds((double)Math.Max(0, bytes) / m_policy.MinBytesPerSecond);
    }

    #endregion

    #region Properties

    /// <summary>What this machine fetches from.</summary>
    public ShardOriginPolicy Origins => m_origins;

    #endregion

    #region Types

    /// <summary>One answer that was not a success: the status and how long the origin asked to wait.</summary>
    private sealed class StatusException(HttpStatusCode status, TimeSpan? retryAfter) : Exception
    {
        public HttpStatusCode Status { get; } = status;

        public TimeSpan? RetryAfter { get; } = retryAfter;
    }

    #endregion
}
