using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// The node's side of delivery: a package of problems is downloaded from the blob channel and a
/// package of results is uploaded to it. The channel moves a blob in chunks and resumes after a
/// break; on top of that a transfer that fails is tried again here for about five minutes. A few
/// downloads run at once, so the packages of a share arrive ahead of the work instead of one before
/// each; the first transfer of all goes alone (<see cref="SmtBlobGate"/>), and retries are spread so
/// that transfers that failed together do not come back together. Every attempt has a time to
/// finish in, by the size of what it moves (<see cref="TransferTimeout"/>).
/// </summary>
internal static class SmtPackageTransfer
{
    #region Constants

    /// <summary>What every attempt of a transfer gets, whatever it moves.</summary>
    public static readonly TimeSpan TRANSFER_BASE_TIME = TimeSpan.FromMinutes(2);

    /// <summary>The slowest a transfer may move its bytes before the attempt counts as failed: 32 KiB a second.</summary>
    public const long TRANSFER_SLOWEST_BYTES_PER_SECOND = 32 * 1024;

    /// <summary>Package downloads in flight on this node at once.</summary>
    private const int FETCH_DEGREE = 4;

    /// <summary>How far a retry delay is moved at random, as a share of it.</summary>
    private const double SPREAD_SHARE = 0.25;

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

    private static readonly SemaphoreSlim FETCH = new(FETCH_DEGREE, FETCH_DEGREE);

    private static readonly SmtBlobGate GATE = new();

    #endregion

    #region Functions

    /// <summary>Downloads a package of problems into the node's blob cache.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="package">The reference.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The local path of the blob.</returns>
    /// <exception cref="SmtPackageException">The download failed at every attempt (<see cref="SmtNodeFaults.DOWNLOAD_FAILED"/>).</exception>
    public static Task<string> FetchAsync(IWitBlobService blobs, SmtTaskPackageData package, ILogger logger, CancellationToken cancellationToken)
    {
        return FetchAsync(blobs, package, logger, cancellationToken, RETRY_DELAYS);
    }

    /// <summary>Downloads a package of problems, waiting between the attempts as told: for tests of what a failure says.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="package">The reference.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <param name="retryDelays">The waits between the attempts.</param>
    /// <returns>The local path of the blob.</returns>
    /// <exception cref="SmtPackageException">The download failed at every attempt (<see cref="SmtNodeFaults.DOWNLOAD_FAILED"/>).</exception>
    public static async Task<string> FetchAsync(IWitBlobService blobs, SmtTaskPackageData package, ILogger logger, CancellationToken cancellationToken, IReadOnlyList<TimeSpan> retryDelays)
    {
        await FETCH.WaitAsync(cancellationToken);
        try
        {
            return await WithRetryAsync(() => blobs.GetLocalPathAsync(package.ProblemsBlobId), TransferTimeout(package.SizeBytes), "download of a package of problems",
                SmtNodeFaults.DOWNLOAD_FAILED, logger, cancellationToken, retryDelays);
        }
        finally
        {
            FETCH.Release();
        }
    }

    /// <summary>Uploads a package of results from the file it was written to.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="path">The package file (<see cref="SmtResultPackageWriter"/>), complete.</param>
    /// <param name="ticket">The ticket of the package of problems the rows answer.</param>
    /// <param name="rows">The rows in the package.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The reference to return to the host.</returns>
    /// <exception cref="SmtPackageException">The upload failed at every attempt (<see cref="SmtNodeFaults.UPLOAD_FAILED"/>).</exception>
    public static async Task<SmtResultPackageData> UploadResultsAsync(IWitBlobService blobs, string path, string ticket, int rows, ILogger logger, CancellationToken cancellationToken)
    {
        var size = new FileInfo(path).Length;
        var sha256 = SmtPackageCodec.HashFile(path);
        var blobId = await WithRetryAsync(() => blobs.UploadFileAsync(path), TransferTimeout(size), "upload of a package of results",
            SmtNodeFaults.UPLOAD_FAILED, logger, cancellationToken, RETRY_DELAYS);

        return new SmtResultPackageData
        {
            ResultsBlobId = blobId,
            Sha256 = sha256,
            SizeBytes = size,
            ResultCount = rows,
            Ticket = ticket
        };
    }

    /// <summary>
    /// The time one attempt of a transfer has: two minutes, and the time its bytes take at the
    /// slowest rate a transfer may have.
    /// </summary>
    /// <param name="bytes">The bytes the transfer moves.</param>
    /// <returns>The time.</returns>
    public static TimeSpan TransferTimeout(long bytes)
    {
        return TRANSFER_BASE_TIME + TimeSpan.FromSeconds(Math.Max(0, bytes) / (double)TRANSFER_SLOWEST_BYTES_PER_SECOND);
    }

    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> transfer, TimeSpan timeout, string what, string fault, ILogger logger, CancellationToken cancellationToken, IReadOnlyList<TimeSpan> retryDelays)
    {
        var clock = Stopwatch.StartNew();

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await GATE.RunAsync(transfer, timeout, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && attempt < retryDelays.Count)
            {
                var delay = Spread(retryDelays[attempt]);
                logger.LogWarning(exception, "Smt: the {What} failed (attempt {Attempt} of {Attempts}); the next one starts in {Delay:F1} s",
                    what, attempt + 1, retryDelays.Count + 1, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The last attempt. What the blob channel said stays in the node's log: the words
                // that travel say only what was tried and for how long, since a channel's own
                // words can name a folder or an address of the node.
                logger.LogError(exception, "Smt: the {What} failed {Attempts} time(s) over {Elapsed:F0} s", what, attempt + 1, clock.Elapsed.TotalSeconds);
                throw new SmtPackageException(fault, $"the {what} failed {attempt + 1} time(s) over {clock.Elapsed.TotalSeconds:F0} s");
            }
        }
    }

    /// <summary>A delay with a quarter of it taken off or added at random: transfers that failed together do not come back together.</summary>
    private static TimeSpan Spread(TimeSpan delay)
    {
        return delay * (1 - SPREAD_SHARE + 2 * SPREAD_SHARE * Random.Shared.NextDouble());
    }

    #endregion
}
