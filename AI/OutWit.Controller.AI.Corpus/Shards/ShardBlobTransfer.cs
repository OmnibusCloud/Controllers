using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// A node's transfers over the blob channel: the language model coming in, the products going
/// out. The channel moves a blob in chunks and resumes after a break; on top of that a
/// transfer that fails is tried again here for about five minutes, because a link that drops
/// for a moment (a server restart, a reconnect) must not turn into a shard without a result; a
/// transfer that still fails returns its shard as a node fault. The first transfer of all goes alone
/// (<see cref="ShardBlobGate"/>), and retries are spread so that transfers that failed
/// together do not come back together. Every attempt has a time to finish in, by the size of
/// what it moves (<see cref="TransferTimeout"/>): the channel's calls take no token, so an
/// attempt that never returns frees the gate when its time is up. Its call goes on, and no other
/// attempt of the same transfer starts beside it: it is waited for, the job's token bounding the
/// wait, and what it comes to is that attempt's outcome — a file is never sent twice at once. A
/// call the job leaves running holds its file (<see cref="HeldBy"/>): the task keeps the file and
/// its room on the disk until the call ends.
/// </summary>
public static class ShardBlobTransfer
{
    #region Constants

    private static readonly TimeSpan[] RETRY_DELAYS =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(40),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(60)
    ];

    /// <summary>How far a retry delay is moved at random, as a share of it.</summary>
    private const double SPREAD_SHARE = 0.25;

    /// <summary>What every attempt of a transfer gets, whatever it moves.</summary>
    public static readonly TimeSpan TRANSFER_BASE_TIME = TimeSpan.FromMinutes(2);

    /// <summary>The slowest a transfer may move its bytes before the attempt counts as failed: 32 KiB a second.</summary>
    public const long TRANSFER_SLOWEST_BYTES_PER_SECOND = 32 * 1024;

    private static readonly ShardBlobGate GATE = new();

    /// <summary>The calls a job left running, by the full path of the file each moves.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task> HELD = new(StringComparer.Ordinal);

    #endregion

    #region Properties

    /// <summary>The waits between the attempts of a transfer; a test of a transfer that never goes through shortens them.</summary>
    internal static IReadOnlyList<TimeSpan> Delays { get; set; } = RETRY_DELAYS;

    /// <summary>What every attempt gets beside the time of its bytes; a test of a transfer that never returns shortens it.</summary>
    internal static TimeSpan BaseTime { get; set; } = TRANSFER_BASE_TIME;

    #endregion

    #region Functions

    /// <summary>Resolves a blob to a local file, downloading it when the node's cache does not hold it.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="blobId">The blob.</param>
    /// <param name="maxBytes">The most bytes the blob may be (a task package's size, a language model's bound): what an attempt's time is sized for.</param>
    /// <param name="logger">Receives a warning per failed attempt; null for none.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The local path of the blob.</returns>
    public static Task<string> DownloadAsync(IWitBlobService blobs, Guid blobId, long maxBytes, ILogger? logger, CancellationToken cancellationToken)
    {
        return WithRetryAsync(() => blobs.GetLocalPathAsync(blobId), TransferTimeout(maxBytes), "download of a blob", logger, cancellationToken);
    }

    /// <summary>Uploads a file as a blob.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="localFilePath">The file.</param>
    /// <param name="logger">Receives a warning per failed attempt; null for none.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The blob's id.</returns>
    public static Task<Guid> UploadAsync(IWitBlobService blobs, string localFilePath, ILogger? logger, CancellationToken cancellationToken)
    {
        return WithRetryAsync(() => blobs.UploadFileAsync(localFilePath), TransferTimeout(Size(localFilePath)), "upload of a product", logger, cancellationToken, localFilePath);
    }

    /// <summary>The upload call that still reads a file after its job was cancelled; null when none does.</summary>
    /// <param name="localFilePath">The file.</param>
    /// <returns>The call, which ends on its own; null when no call holds the file.</returns>
    public static Task? HeldBy(string localFilePath)
    {
        return HELD.TryGetValue(Key(localFilePath), out var call) && !call.IsCompleted ? call : null;
    }

    /// <summary>
    /// The time one attempt of a transfer has: two minutes, and the time its bytes take at the
    /// slowest rate a transfer may have.
    /// </summary>
    /// <param name="bytes">The bytes the transfer moves.</param>
    /// <returns>The time.</returns>
    public static TimeSpan TransferTimeout(long bytes)
    {
        return BaseTime + TimeSpan.FromSeconds(Math.Max(0, bytes) / (double)TRANSFER_SLOWEST_BYTES_PER_SECOND);
    }

    /// <summary>The size of a file that is about to be uploaded; one that cannot be asked is sized as nothing (its upload fails on its own).</summary>
    private static long Size(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> transfer, TimeSpan timeout, string what, ILogger? logger, CancellationToken cancellationToken, string? file = null)
    {
        // A warning that cannot be written must not fail the transfer it is about.
        logger = logger == null ? null : CorpusLogger.Wrap(logger);

        var clock = Stopwatch.StartNew();

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<T>? abandoned = null;
            Exception failure;
            try
            {
                return await GATE.RunAsync(transfer, timeout, cancellationToken, running => abandoned = running);
            }
            catch (OperationCanceledException)
            {
                Hold(file, abandoned);
                throw;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            // An attempt that ran out of its time goes on outside the gate (its call takes no
            // token): it is waited for, and no other attempt starts beside it. What it comes to
            // is this attempt's outcome.
            if (abandoned != null)
            {
                try
                {
                    return await abandoned.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Hold(file, abandoned);
                    throw;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (attempt >= Delays.Count)
            {
                // The last attempt. The caller turns this into a node fault with a fixed code; the
                // text stays on the node.
                throw new IOException($"The {what} failed {attempt + 1} time(s) over {clock.Elapsed.TotalSeconds:F0} s.", failure);
            }

            var delay = Spread(Delays[attempt]);

            // The kind of the failure, not its message: a transfer's error can name a path or a host.
            logger?.LogWarning("Corpus: the {What} failed ({Kind}; attempt {Attempt} of {Attempts}); the next one starts in {Delay:F1} s",
                what, failure.GetType().Name, attempt + 1, Delays.Count + 1, delay.TotalSeconds);

            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>Records a call the job left running on a file, until it ends.</summary>
    private static void Hold(string? file, Task? call)
    {
        if (file == null || call == null || call.IsCompleted)
            return;

        var key = Key(file);
        HELD[key] = call;
        _ = call.ContinueWith(_ => HELD.TryRemove(new KeyValuePair<string, Task>(key, call)), TaskScheduler.Default);
    }

    private static string Key(string file)
    {
        try
        {
            return Path.GetFullPath(file);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return file;
        }
    }

    /// <summary>
    /// A delay with a quarter of it taken off or added at random: transfers that failed
    /// together do not come back together.
    /// </summary>
    private static TimeSpan Spread(TimeSpan delay)
    {
        return delay * (1 - SPREAD_SHARE + 2 * SPREAD_SHARE * Random.Shared.NextDouble());
    }

    #endregion
}
