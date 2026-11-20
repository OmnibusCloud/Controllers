using System.Globalization;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Delivery;

/// <summary>
/// One task package on a node, start to finish: the task blob is fetched over the blob channel
/// and held to the hash and the size its reference names, its tasks are run a few at a time
/// (<see cref="ShardTaskRunner"/>: each comes back as a result, a verdict on its shard or a fault
/// of this node), and the results go back as one blob.
/// <para>
/// The package always comes back as an answer under its ticket. When the node cannot answer it as
/// a whole — the task blob did not arrive or is not the one the reference names, the results could
/// not be written or uploaded — the answer carries the fault's fixed code and no blob, and the
/// server sends the package's shards out once more: nothing of the failure's own words leaves the
/// node.
/// </para>
/// </summary>
public static class CorpusPackageRunner
{
    #region Functions

    /// <summary>Runs one task package.</summary>
    /// <param name="package">The package's reference.</param>
    /// <param name="fetcher">Fetches the shards.</param>
    /// <param name="blobs">Brings the task blob and the language model, receives the products and the results.</param>
    /// <param name="scope">A folder of this execution's own; the caller deletes it.</param>
    /// <param name="jobId">The job, for the node's picture of its share.</param>
    /// <param name="report">The host's progress sink (fraction, stage), or null.</param>
    /// <param name="logger">The node's own log: what a fault was, in the detail no result carries.</param>
    /// <param name="beforeFetch">Called before each shard is fetched: throws when the node has refused the job's batch meanwhile. Null for none.</param>
    /// <param name="cancellationToken">Cancels the package.</param>
    /// <returns>The answer: a reference to the results blob, or a fault of this node.</returns>
    /// <exception cref="ShardBatchRefusedException">The node refused the batch while the package ran.</exception>
    public static async Task<CorpusResultPackageData> RunAsync(CorpusTaskPackageData package, ShardFetcher fetcher, IWitBlobService blobs, string scope, Guid jobId, Action<double, string>? report, ILogger logger, Action? beforeFetch, CancellationToken cancellationToken)
    {
        // Whoever the caller is, the node's log is written through the guard: a line that cannot be written is not a fault of the package.
        logger = CorpusLogger.Wrap(logger);

        List<CorpusTaskData> tasks;
        try
        {
            tasks = await ReadTasksAsync(package, blobs, logger, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fault(package, CorpusResultCodes.FAULT_TASKS, exception, logger);
        }

        // The whole package enters the node's picture of its share at once; each shard leaves it when it is over.
        var places = tasks.Select(task => ShardShareProgress.Enter(jobId, task.Source?.SizeBytes ?? 0, report, logger)).ToArray();
        var results = new CorpusResultData[tasks.Count];
        try
        {
            // As many at a time as the node holds on disk: the rest of the package waits with nothing started.
            var options = new ParallelOptions { MaxDegreeOfParallelism = ShardTaskRunner.Window, CancellationToken = cancellationToken };
            await Parallel.ForEachAsync(Enumerable.Range(0, tasks.Count), options, async (index, token) =>
            {
                var directory = Path.Combine(scope, index.ToString("D", CultureInfo.InvariantCulture));
                try
                {
                    results[index] = await ShardTaskRunner.RunAsync(tasks[index], fetcher, blobs, directory, places[index], logger, null, beforeFetch, token);
                }
                finally
                {
                    places[index].Dispose();

                    // Not on the result's path: a delete can wait on the node's file system far longer than the work took.
                    ShardCleanup.Schedule(() => TryDeleteDirectory(directory));
                }
            });
        }
        finally
        {
            foreach (var place in places)
                place.Dispose();
        }

        var path = Path.Combine(scope, CorpusPackageCodec.RESULTS_FILE_NAME);
        try
        {
            Directory.CreateDirectory(scope);
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            CorpusPackageCodec.WriteResults(file, results);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fault(package, ShardLocalFaultException.IsLocal(exception) ? CorpusResultCodes.FAULT_DISK : CorpusResultCodes.FAULT_INTERNAL, exception, logger);
        }

        try
        {
            return new CorpusResultPackageData
            {
                Ticket = package.Ticket ?? "",
                ResultsBlobId = await ShardBlobTransfer.UploadAsync(blobs, path, logger, cancellationToken)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fault(package, CorpusResultCodes.FAULT_UPLOAD, exception, logger);
        }
    }

    /// <summary>The answer of a node that could not answer a package as a whole.</summary>
    /// <param name="package">The package's reference.</param>
    /// <param name="code">The fault's code; anything that is no fault code becomes <c>internal</c>.</param>
    /// <returns>The answer: the package's ticket, the code, no blob.</returns>
    public static CorpusResultPackageData Fault(CorpusTaskPackageData package, string code)
    {
        return new CorpusResultPackageData
        {
            Ticket = package.Ticket ?? "",
            Fault = CorpusResultCodes.IsFault(code) ? code : CorpusResultCodes.FAULT_INTERNAL
        };
    }

    #endregion

    #region Tools

    /// <summary>
    /// Brings the package's tasks: the blob as its reference names it (size and hash), read
    /// within the node's own bound on a package, every task under a ticket and on the version
    /// the reference states.
    /// </summary>
    private static async Task<List<CorpusTaskData>> ReadTasksAsync(CorpusTaskPackageData package, IWitBlobService blobs, ILogger logger, CancellationToken cancellationToken)
    {
        if (package.ShardCount is < 1 or > CorpusPackagePlanner.MAX_PACKAGE_SHARDS)
            throw new InvalidDataException("The package holds more shards than a package may.");

        var path = await ShardBlobTransfer.DownloadAsync(blobs, package.TasksBlobId, package.SizeBytes, logger, cancellationToken);

        if (new FileInfo(path).Length != package.SizeBytes || !string.Equals(CorpusPackageCodec.HashFile(path), package.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("The task blob is not the one its reference names.");

        var tasks = CorpusPackageCodec.ReadTasks(path, package.ShardCount);
        if (tasks.Count != package.ShardCount)
            throw new InvalidDataException("The task blob holds another number of tasks than its reference names.");

        foreach (var task in tasks)
        {
            if (!CorpusTickets.IsWellFormed(task.Ticket) || !string.Equals(task.RequiredControllerVersion, package.RequiredControllerVersion, StringComparison.Ordinal))
                throw new InvalidDataException("A task of the package is not one the server made for it.");
        }

        return tasks;
    }

    private static CorpusResultPackageData Fault(CorpusTaskPackageData package, string code, Exception exception, ILogger logger)
    {
        // The kind of the failure and its system code, not its message: a message names paths and hosts.
        logger.LogWarning("Corpus.ProcessPackage: a fault of this node, {Code} ({Kind}, 0x{HResult:X8}); the package's {Shards} shard(s) go back unanswered.",
            code, exception.GetType().Name, exception.HResult, package.ShardCount);

        return Fault(package, code);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // It goes with the package's scope.
        }
    }

    #endregion
}
