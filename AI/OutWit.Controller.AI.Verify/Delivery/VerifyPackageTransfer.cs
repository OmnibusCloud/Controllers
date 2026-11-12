using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// The node's side of delivery: a task package is downloaded from the blob channel and a
/// result package is uploaded to it. The channel moves a blob in chunks and resumes after a
/// break; on top of that a transfer that fails is tried again here for about five minutes,
/// because one failed item fails the node's whole share. A few downloads run at once, so the
/// packages of a share arrive ahead of the work instead of one before each; the first
/// transfer of all goes alone (<see cref="VerifyBlobGate"/>), and retries are spread so that
/// transfers that failed together do not come back together.
/// </summary>
public static class VerifyPackageTransfer
{
    #region Constants

    /// <summary>Package downloads in flight on this node at once.</summary>
    private const int FETCH_DEGREE = 4;

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
    public static async Task<string> FetchAsync(IWitBlobService blobs, VerifyTaskPackageData package, ILogger logger, CancellationToken cancellationToken)
    {
        await FETCH.WaitAsync(cancellationToken);
        try
        {
            return await WithRetryAsync(() => blobs.GetLocalPathAsync(package.TasksBlobId), "download of a task package", logger, cancellationToken);
        }
        finally
        {
            FETCH.Release();
        }
    }

    /// <summary>Reads the task batches of a downloaded package, after checking it is the package the reference names.</summary>
    /// <param name="blobs">The node's blob service, for a package the cache has dropped while it waited for its turn.</param>
    /// <param name="package">The reference.</param>
    /// <param name="path">The local path <see cref="FetchAsync"/> returned.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The task batches, in order.</returns>
    /// <exception cref="InvalidDataException">The blob is not the package the reference names.</exception>
    public static async Task<List<VerifyTaskBatchData>> ReadTasksAsync(IWitBlobService blobs, VerifyTaskPackageData package, string path, ILogger logger, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            path = await FetchAsync(blobs, package, logger, cancellationToken);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        VerifyPackageCodec.EnsureHash(bytes, package.Sha256, "task package");

        var batches = VerifyPackageCodec.ReadTasks(bytes);
        var tasks = batches.Sum(batch => batch.Tasks.Count);
        if (tasks != package.TaskCount)
            throw new InvalidDataException($"The task package holds {tasks} task(s), its reference names {package.TaskCount}.");

        return batches;
    }

    /// <summary>Uploads the result batches of a package as one blob.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="results">The result batches, one per task batch executed.</param>
    /// <param name="logger">Receives a warning per failed attempt.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The reference to return to the host.</returns>
    public static async Task<VerifyResultPackageData> UploadResultsAsync(IWitBlobService blobs, IReadOnlyList<VerifyResultBatchData> results, ILogger logger, CancellationToken cancellationToken)
    {
        var content = VerifyPackageCodec.WriteResults(results);
        var blobId = await WithRetryAsync(() => blobs.UploadBytesAsync(content, VerifyPackageCodec.RESULTS_FILE_NAME), "upload of a result package", logger, cancellationToken);

        return new VerifyResultPackageData
        {
            ResultsBlobId = blobId,
            Sha256 = VerifyPackageCodec.Hash(content),
            SizeBytes = content.Length,
            TaskCount = results.Sum(batch => batch.Results.Count),
            BatchCount = results.Count
        };
    }

    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> transfer, string what, ILogger logger, CancellationToken cancellationToken)
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
                logger.LogWarning(exception, "Verify: the {What} failed (attempt {Attempt} of {Attempts}); the next one starts in {Delay:F1} s",
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
