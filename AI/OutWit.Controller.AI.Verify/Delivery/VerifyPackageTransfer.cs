using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// The node's side of delivery: a task package is downloaded from the blob channel and a
/// result package is uploaded to it. The channel moves a blob in chunks and resumes after a
/// break; on top of that a transfer that fails is tried again here for about five minutes,
/// because one failed item fails the node's whole share. A few downloads run at once, so the
/// packages of a share arrive ahead of the work instead of one before each; the first
/// transfer of all goes alone (<see cref="VerifyBlobGate"/>), and retries are spread so that
/// transfers that failed together do not come back together. Every attempt has a time to
/// finish in, by the size of what it moves (<see cref="TransferTimeout"/>).
/// <para>
/// A node holds what it is sent to the caps itself, whoever made the package: a blob larger
/// than a task package may be, or one that names more tasks than a package may hold, is refused
/// before it is read (<see cref="VerifyBatchRefusedException"/>).
/// </para>
/// </summary>
public static class VerifyPackageTransfer
{
    #region Constants

    /// <summary>Package downloads in flight on this node at once.</summary>
    private const int FETCH_DEGREE = 4;

    /// <summary>What every attempt of a transfer gets, whatever it moves.</summary>
    public static readonly TimeSpan TRANSFER_BASE_TIME = TimeSpan.FromMinutes(2);

    /// <summary>The slowest a transfer may move its bytes before the attempt counts as failed: 32 KiB a second.</summary>
    public const long TRANSFER_SLOWEST_BYTES_PER_SECOND = 32 * 1024;

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

    private static readonly SemaphoreSlim FETCH = new(FETCH_DEGREE, FETCH_DEGREE);

    private static readonly VerifyBlobGate GATE = new();

    #endregion

    #region Functions

    /// <summary>Downloads a task package into the node's blob cache.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="package">The reference.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The local path of the blob.</returns>
    /// <exception cref="IOException">The download failed at every attempt; the message is fixed words.</exception>
    public static Task<string> FetchAsync(IWitBlobService blobs, VerifyTaskPackageData package, ILogger logger, CancellationToken cancellationToken)
    {
        return FetchAsync(blobs, package, logger, cancellationToken, RETRY_DELAYS);
    }

    /// <summary>Downloads a task package, waiting between the attempts as told: for tests of what a failure says.</summary>
    internal static async Task<string> FetchAsync(IWitBlobService blobs, VerifyTaskPackageData package, ILogger logger, CancellationToken cancellationToken, IReadOnlyList<TimeSpan> retryDelays)
    {
        await FETCH.WaitAsync(cancellationToken);
        try
        {
            return await WithRetryAsync(() => blobs.GetLocalPathAsync(package.TasksBlobId), TransferTimeout(package.SizeBytes), "download of a task package", logger, cancellationToken, retryDelays);
        }
        finally
        {
            FETCH.Release();
        }
    }

    /// <summary>Reads the task batches of a downloaded package, after checking it is the package the reference names and no larger than a package may be.</summary>
    /// <param name="blobs">The node's blob service, for a package the cache has dropped while it waited for its turn.</param>
    /// <param name="package">The reference.</param>
    /// <param name="path">The local path <see cref="FetchAsync"/> returned.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The task batches, in order.</returns>
    /// <exception cref="InvalidDataException">The blob is not the package the reference names.</exception>
    /// <exception cref="VerifyBatchRefusedException">The package is larger than a node takes.</exception>
    public static async Task<List<VerifyTaskBatchData>> ReadTasksAsync(IWitBlobService blobs, VerifyTaskPackageData package, string path, ILogger logger, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            path = await FetchAsync(blobs, package, logger, cancellationToken);

        if (new FileInfo(path).Length > VerifyPackageCodec.MAX_TASKS_IMAGE_BYTES)
            throw new VerifyBatchRefusedException("the task package is larger than a node takes", null);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        VerifyPackageCodec.EnsureHash(bytes, package.Sha256, "task package");

        var batches = VerifyPackageCodec.ReadTasks(bytes);
        var tasks = batches.Sum(batch => batch.Tasks.Count);
        if (tasks != package.TaskCount)
            throw new InvalidDataException($"The task package holds {tasks} task(s), its reference names {package.TaskCount}.");

        if (tasks > VerifyPackagePlanner.MAX_PACKAGE_TASKS)
            throw new VerifyBatchRefusedException($"the task package holds {tasks} tasks; a node takes no more than {VerifyPackagePlanner.MAX_PACKAGE_TASKS}", null);

        return batches;
    }

    /// <summary>Uploads a result package from the file it was written to.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="path">The package file (<see cref="VerifyResultPackageWriter"/>), complete.</param>
    /// <param name="ticket">The ticket of the task package the results answer.</param>
    /// <param name="taskCount">The results in the package.</param>
    /// <param name="batchCount">The batches in the package.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The reference to return to the host.</returns>
    /// <exception cref="IOException">The upload failed at every attempt; the message is fixed words.</exception>
    public static async Task<VerifyResultPackageData> UploadResultsAsync(IWitBlobService blobs, string path, string ticket, int taskCount, int batchCount, ILogger logger, CancellationToken cancellationToken)
    {
        var size = new FileInfo(path).Length;
        var sha256 = VerifyPackageCodec.HashFile(path);
        var blobId = await WithRetryAsync(() => blobs.UploadFileAsync(path), TransferTimeout(size), "upload of a result package", logger, cancellationToken, RETRY_DELAYS);

        return new VerifyResultPackageData
        {
            ResultsBlobId = blobId,
            Sha256 = sha256,
            SizeBytes = size,
            TaskCount = taskCount,
            BatchCount = batchCount,
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

    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> transfer, TimeSpan timeout, string what, ILogger logger, CancellationToken cancellationToken, IReadOnlyList<TimeSpan> retryDelays)
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
                logger.LogWarning(exception, "Verify: the {What} failed (attempt {Attempt} of {Attempts}); the next one starts in {Delay:F1} s",
                    what, attempt + 1, retryDelays.Count + 1, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The last attempt. What the blob channel said stays in the node's log: the
                // error that travels says only what was tried and for how long, since a channel's
                // own words can name a folder or an address of the node.
                logger.LogError(exception, "Verify: the {What} failed {Attempts} time(s) over {Elapsed:F0} s", what, attempt + 1, clock.Elapsed.TotalSeconds);
                throw new IOException($"The {what} failed {attempt + 1} time(s) over {clock.Elapsed.TotalSeconds:F0} s.");
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
