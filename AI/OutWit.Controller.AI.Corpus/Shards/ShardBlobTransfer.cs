using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// A node's transfers over the blob channel: the language model coming in, the products going
/// out. The channel moves a blob in chunks and resumes after a break; on top of that a
/// transfer that fails is tried again here for about five minutes, because a link that drops
/// for a moment (a server restart, a reconnect) must not turn into a failed shard, and a
/// failed upload fails the node's whole share. The first transfer of all goes alone
/// (<see cref="ShardBlobGate"/>), and retries are spread so that transfers that failed
/// together do not come back together.
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

    private static readonly ShardBlobGate GATE = new();

    #endregion

    #region Functions

    /// <summary>Resolves a blob to a local file, downloading it when the node's cache does not hold it.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="blobId">The blob.</param>
    /// <param name="logger">Receives a warning per failed attempt; null for none.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The local path of the blob.</returns>
    public static Task<string> DownloadAsync(IWitBlobService blobs, Guid blobId, ILogger? logger, CancellationToken cancellationToken)
    {
        return WithRetryAsync(() => blobs.GetLocalPathAsync(blobId), "download of a blob", logger, cancellationToken);
    }

    /// <summary>Uploads a file as a blob.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="localFilePath">The file.</param>
    /// <param name="logger">Receives a warning per failed attempt; null for none.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The blob's id.</returns>
    public static Task<Guid> UploadAsync(IWitBlobService blobs, string localFilePath, ILogger? logger, CancellationToken cancellationToken)
    {
        return WithRetryAsync(() => blobs.UploadFileAsync(localFilePath), "upload of a product", logger, cancellationToken);
    }

    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> transfer, string what, ILogger? logger, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await GATE.RunAsync(transfer, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && attempt < RETRY_DELAYS.Length)
            {
                var delay = Spread(RETRY_DELAYS[attempt]);
                logger?.LogWarning(exception, "Corpus: the {What} failed (attempt {Attempt} of {Attempts}); the next one starts in {Delay:F1} s",
                    what, attempt + 1, RETRY_DELAYS.Length + 1, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The last attempt: the error a submitter or an operator reads is the only trace
                // a node leaves of it, so it says how long the transfer was tried.
                throw new IOException($"The {what} failed {attempt + 1} time(s) over {clock.Elapsed.TotalSeconds:F0} s: {exception.Message}", exception);
            }
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
