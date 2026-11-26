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
/// <para>
/// The channel's calls take no token, so an attempt that ran out of its time goes on. It is waited
/// for a little longer (<see cref="LATE_TIME"/>) before anything else is tried: what it comes to
/// in that time is the transfer's result, so no second download of the blob starts beside it and
/// no second upload leaves a blob nobody names. A call that is still running after that is left
/// to itself and the next attempt starts; it cannot be stopped, and waiting for it for good would
/// hand the transfer to a dead channel. A download keeps its place among the downloads at once
/// until the calls it left running have ended, for the same time at most, so that a job that is
/// cancelled does not leave its downloads running beside those of the next.
/// </para>
/// </summary>
internal static class SmtPackageTransfer
{
    #region Constants

    /// <summary>What every attempt of a transfer gets, whatever it moves.</summary>
    public static readonly TimeSpan TRANSFER_BASE_TIME = TimeSpan.FromMinutes(2);

    /// <summary>The slowest a transfer may move its bytes before the attempt counts as failed: 32 KiB a second.</summary>
    public const long TRANSFER_SLOWEST_BYTES_PER_SECOND = 32 * 1024;

    /// <summary>
    /// How long an attempt that ran out of its time is still waited for, and how long a download
    /// keeps its place for the calls it left running. A transfer whose calls end in their time never
    /// meets it.
    /// </summary>
    public static readonly TimeSpan LATE_TIME = TimeSpan.FromSeconds(30);

    /// <summary>Package downloads in flight on this node at once.</summary>
    public const int FETCH_DEGREE = 4;

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

    /// <summary>Downloads a package of problems with the times of its attempts as told: for tests of what a failure says and of an attempt that does not end.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="package">The reference.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <param name="retryDelays">The waits between the attempts.</param>
    /// <param name="attemptTime">The time one attempt has; null for the time its size gives it (<see cref="TransferTimeout"/>).</param>
    /// <param name="lateTime">The time an attempt that ran out of its own is still waited for; null for <see cref="LATE_TIME"/>.</param>
    /// <returns>The local path of the blob.</returns>
    /// <exception cref="SmtPackageException">The download failed at every attempt (<see cref="SmtNodeFaults.DOWNLOAD_FAILED"/>).</exception>
    public static async Task<string> FetchAsync(IWitBlobService blobs, SmtTaskPackageData package, ILogger logger, CancellationToken cancellationToken, IReadOnlyList<TimeSpan> retryDelays,
        TimeSpan? attemptTime = null, TimeSpan? lateTime = null)
    {
        var late = lateTime ?? LATE_TIME;
        var left = new List<Task>();

        await FETCH.WaitAsync(cancellationToken);
        try
        {
            return await WithRetryAsync(() => blobs.GetLocalPathAsync(package.ProblemsBlobId), attemptTime ?? TransferTimeout(package.SizeBytes), late, "download of a package of problems",
                SmtNodeFaults.DOWNLOAD_FAILED, logger, cancellationToken, retryDelays, left);
        }
        finally
        {
            GiveBack(left, late);
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
    public static Task<SmtResultPackageData> UploadResultsAsync(IWitBlobService blobs, string path, string ticket, int rows, ILogger logger, CancellationToken cancellationToken)
    {
        return UploadResultsAsync(blobs, path, ticket, rows, logger, cancellationToken, RETRY_DELAYS);
    }

    /// <summary>Uploads a package of results with the times of its attempts as told: for tests of an attempt that does not end.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="path">The package file, complete.</param>
    /// <param name="ticket">The ticket of the package of problems the rows answer.</param>
    /// <param name="rows">The rows in the package.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <param name="retryDelays">The waits between the attempts.</param>
    /// <param name="attemptTime">The time one attempt has; null for the time its size gives it (<see cref="TransferTimeout"/>).</param>
    /// <param name="lateTime">The time an attempt that ran out of its own is still waited for; null for <see cref="LATE_TIME"/>.</param>
    /// <returns>The reference to return to the host.</returns>
    /// <exception cref="SmtPackageException">The upload failed at every attempt (<see cref="SmtNodeFaults.UPLOAD_FAILED"/>).</exception>
    public static async Task<SmtResultPackageData> UploadResultsAsync(IWitBlobService blobs, string path, string ticket, int rows, ILogger logger, CancellationToken cancellationToken,
        IReadOnlyList<TimeSpan> retryDelays, TimeSpan? attemptTime = null, TimeSpan? lateTime = null)
    {
        var size = new FileInfo(path).Length;
        var sha256 = SmtPackageCodec.HashFile(path);
        var blobId = await WithRetryAsync(() => blobs.UploadFileAsync(path), attemptTime ?? TransferTimeout(size), lateTime ?? LATE_TIME, "upload of a package of results",
            SmtNodeFaults.UPLOAD_FAILED, logger, cancellationToken, retryDelays, null);

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

    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> transfer, TimeSpan timeout, TimeSpan late, string what, string fault, ILogger logger, CancellationToken cancellationToken,
        IReadOnlyList<TimeSpan> retryDelays, List<Task>? left)
    {
        var clock = Stopwatch.StartNew();

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<T>? call = null;
            Exception failure;
            try
            {
                return await GATE.RunAsync(() => call = transfer(), timeout, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Leave(call, left);
                throw;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (call is { IsCompleted: false })
            {
                // The attempt ran out of its time and its call goes on. Another attempt beside
                // it would be a second download of the same blob or a second upload of the same
                // rows, so the call is waited for a little longer first.
                logger.LogWarning("Smt: the {What} did not end in its time of {Time:F0} s (attempt {Attempt} of {Attempts}); it cannot be stopped, and is waited for up to {Late:F0} s more before anything else is tried",
                    what, timeout.TotalSeconds, attempt + 1, retryDelays.Count + 1, late.TotalSeconds);

                await ((Task)call).WaitAsync(late, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                if (cancellationToken.IsCancellationRequested)
                {
                    Leave(call, left);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            if (call is { IsCompleted: true })
            {
                // What the call came to is what the attempt came to, in its time or after it.
                try
                {
                    var result = await call;
                    logger.LogInformation("Smt: the {What} ended after its time (attempt {Attempt} of {Attempts}); what it came to is taken", what, attempt + 1, retryDelays.Count + 1);
                    return result;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failure = exception;
                }
            }
            else
                Leave(call, left);

            if (attempt >= retryDelays.Count)
            {
                // The last attempt. What the blob channel said stays in the node's log: the words
                // that travel say only what was tried and for how long, since a channel's own
                // words can name a folder or an address of the node.
                logger.LogError(failure, "Smt: the {What} failed {Attempts} time(s) over {Elapsed:F0} s", what, attempt + 1, clock.Elapsed.TotalSeconds);
                throw new SmtPackageException(fault, $"the {what} failed {attempt + 1} time(s) over {clock.Elapsed.TotalSeconds:F0} s");
            }

            var delay = Spread(retryDelays[attempt]);
            logger.LogWarning(failure, "Smt: the {What} failed (attempt {Attempt} of {Attempts}); the next one starts in {Delay:F1} s",
                what, attempt + 1, retryDelays.Count + 1, delay.TotalSeconds);

            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>Notes a call that still runs although its attempt is over, for the place of the download it belongs to.</summary>
    private static void Leave(Task? call, List<Task>? left)
    {
        if (call is { IsCompleted: false })
            left?.Add(call);
    }

    /// <summary>
    /// Gives a download's place among the downloads at once back: now when no call of it still
    /// runs, as every download that went well or failed in its time does; otherwise when the
    /// calls it left running have ended, and after the late time at the latest, since a call
    /// that never ends must not hold a place for the life of the process.
    /// </summary>
    private static void GiveBack(List<Task> left, TimeSpan late)
    {
        left.RemoveAll(static call => call.IsCompleted);
        if (left.Count == 0)
        {
            FETCH.Release();
            return;
        }

        // What each call comes to was looked at where it was given up (SmtBlobGate); here only
        // their end counts.
        var ended = Task.WhenAll(left.Select(static call => call.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)));
        _ = Task.WhenAny(ended, Task.Delay(late)).ContinueWith(static _ => FETCH.Release(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>A delay with a quarter of it taken off or added at random: transfers that failed together do not come back together.</summary>
    private static TimeSpan Spread(TimeSpan delay)
    {
        return delay * (1 - SPREAD_SHARE + 2 * SPREAD_SHARE * Random.Shared.NextDouble());
    }

    #endregion
}
