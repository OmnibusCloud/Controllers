using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Controller.AI.Corpus.Warc;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// One task on a node, start to finish: the allowlist re-check against the task and the
/// machine's own policy, the pinned model, the capped fetch, the pipeline, the uploads (none
/// when the pipeline keeps statistics and hashes only: the text and the signatures are deleted
/// with the task's folder, the result carries their hashes and the counters). The task always
/// comes back as a result, with the task's ticket, and it is one of two kinds:
/// <list type="bullet">
/// <item>a <b>verdict</b> on the shard, which any honest node would give for the same bytes — it
/// completed, its origin is not allowed, the origin answered and did not give it, it is too large,
/// it is not a WARC file — with a fixed code, never a text;</item>
/// <item>a <b>node fault</b> (<see cref="CorpusShardVerdict.NodeFault"/>): this node's disk, its
/// link, its memory, its PDF sandbox's wall-clock backstop, an upload that did not go through, an
/// error inside the node. The shard goes back unanswered with the fault's code, and the script
/// sends it out once more; the node's other shards are not lost with it.</item>
/// </list>
/// A node holds only a few shards on disk at a time: a task takes a place in the node's window
/// (<see cref="FETCH_AHEAD"/> beyond the shards it processes at once) before it fetches and gives
/// it back when its work folder can go, so a share of hundreds of shards never sits on the disk
/// together. The shard and its products are deleted before the place is given back; the rest of
/// the folder goes with the caller's scope.
/// </summary>
public static class ShardTaskRunner
{
    #region Constants

    /// <summary>
    /// Shards a node holds fetched and waiting beyond the ones it processes: enough that a
    /// processing slot never waits for a download, few enough that the disk a node needs is its
    /// degree and two more shards, whatever its share.
    /// </summary>
    public const int FETCH_AHEAD = 2;

    private const string SHARD_FILE = "shard.gz";
    private const string TEXT_FILE = "text.jsonl.zst";
    private const string SIGNATURES_FILE = "signatures.bin";

    #endregion

    #region Fields

    /// <summary>The node's degree (cores, memory). It stays on the node: no result says it.</summary>
    private static readonly int DEGREE = ShardDegree.ForCurrentMachine();

    /// <summary>Shards processing at once in this process: the node's degree.</summary>
    private static readonly SemaphoreSlim PROCESSING = new(DEGREE);

    /// <summary>Shards on the node's disk at once, fetching, waiting or processing. The client starts every task of a parallel batch together; the rest wait here, with nothing fetched.</summary>
    private static readonly SemaphoreSlim WINDOW = new(DEGREE + FETCH_AHEAD);

    #endregion

    #region Functions

    /// <summary>The shards this node holds on disk at once: the ones it processes and <see cref="FETCH_AHEAD"/> more. As many tasks of a package are started together.</summary>
    internal static int Window => DEGREE + FETCH_AHEAD;

    /// <summary>Runs one task.</summary>
    /// <param name="task">The task.</param>
    /// <param name="fetcher">Fetches the shard.</param>
    /// <param name="blobs">Resolves the model blob and receives the products.</param>
    /// <param name="workDirectory">A directory of this task's own for the shard and the products; the caller deletes it.</param>
    /// <param name="cancellationToken">Cancels the task.</param>
    /// <returns>The result: a verdict on the shard, or a node fault.</returns>
    public static Task<CorpusResultData> RunAsync(CorpusTaskData task, ShardFetcher fetcher, IWitBlobService blobs, string workDirectory, CancellationToken cancellationToken)
    {
        return RunAsync(task, fetcher, blobs, workDirectory, null, null, cancellationToken);
    }

    /// <summary>Runs one task and reports its delivery to the node's picture of the share.</summary>
    /// <param name="task">The task.</param>
    /// <param name="fetcher">Fetches the shard.</param>
    /// <param name="blobs">Resolves the model blob and receives the products.</param>
    /// <param name="workDirectory">A directory of this task's own for the shard and the products; the caller deletes it.</param>
    /// <param name="ticket">The shard's place in the share's progress, or null for none; the caller disposes it.</param>
    /// <param name="logger">The node's own log: what a fault was, in the detail no result carries; null for none.</param>
    /// <param name="cancellationToken">Cancels the task.</param>
    /// <returns>The result: a verdict on the shard, or a node fault.</returns>
    public static Task<CorpusResultData> RunAsync(CorpusTaskData task, ShardFetcher fetcher, IWitBlobService blobs, string workDirectory, ShardShareTicket? ticket, ILogger? logger, CancellationToken cancellationToken)
    {
        return RunAsync(task, fetcher, blobs, workDirectory, ticket, logger, null, null, cancellationToken);
    }

    /// <summary>Runs one task with a PDF stage of the caller's (tests of a node whose sandbox fails) and a check made before the fetch.</summary>
    /// <param name="task">The task.</param>
    /// <param name="fetcher">Fetches the shard.</param>
    /// <param name="blobs">Resolves the model blob and receives the products.</param>
    /// <param name="workDirectory">A directory of this task's own; the caller deletes it.</param>
    /// <param name="ticket">The shard's place in the share's progress, or null.</param>
    /// <param name="logger">The node's own log; null for none.</param>
    /// <param name="pdfStage">The PDF stage; null for the process-wide sandbox with the work directory.</param>
    /// <param name="beforeFetch">Called when the task has its place in the window, before anything is fetched: throws when the node has refused the task's batch meanwhile. Null for none.</param>
    /// <param name="cancellationToken">Cancels the task.</param>
    /// <returns>The result: a verdict on the shard, or a node fault.</returns>
    internal static async Task<CorpusResultData> RunAsync(CorpusTaskData task, ShardFetcher fetcher, IWitBlobService blobs, string workDirectory, ShardShareTicket? ticket, ILogger? logger, ShardPdfStage? pdfStage, Action? beforeFetch, CancellationToken cancellationToken)
    {
        // The node's log is written through the guard: a line that cannot be written is not a fault of the shard.
        logger = logger == null ? null : CorpusLogger.Wrap(logger);

        var result = new CorpusResultData
        {
            TaskIndex = task.TaskIndex,
            SourceUrl = task.Source?.Url ?? "",
            ControllerVersion = ControllerBuildInfo.VERSION,
            RuntimeTarget = CorpusRuntimeDiagnosticsBuilder.RuntimeTarget(),
            Output = task.Pipeline?.Output ?? CorpusOutputMode.Full,
            Ticket = task.Ticket ?? ""
        };

        try
        {
            return await RunCheckedAsync(task, result, fetcher, blobs, workDirectory, ticket, logger, pdfStage, beforeFetch, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ShardBatchRefusedException)
        {
            // The node refused the batch elsewhere; this task goes with it.
            throw;
        }
        catch (Exception exception)
        {
            // Whatever was not foreseen is this node's, never the shard's: no verdict, no text.
            return Fault(result, FaultCode(exception), exception, logger);
        }
    }

    private static async Task<CorpusResultData> RunCheckedAsync(CorpusTaskData task, CorpusResultData result, ShardFetcher fetcher, IWitBlobService blobs, string workDirectory, ShardShareTicket? ticket, ILogger? logger, ShardPdfStage? pdfStage, Action? beforeFetch, CancellationToken cancellationToken)
    {
        if (task.Source == null || task.Pipeline == null)
            return Verdict(result, CorpusShardVerdict.Failed, CorpusResultCodes.TASK_INVALID);

        if (ShardProcessor.Signs(task.Pipeline) && MinHashParameters.Problem(task.Pipeline.MinHash) != null)
            return Verdict(result, CorpusShardVerdict.Failed, CorpusResultCodes.TASK_INVALID);

        // The task's list AND this machine's policy: the task is written by the submitter and
        // cannot widen what the machine's owner agreed to fetch from.
        if (ShardOrigin.Check(task.Source.Url, task.AllowedOrigins ?? [], out var uri) != null || uri == null)
            return Verdict(result, CorpusShardVerdict.OriginRefused, CorpusResultCodes.ORIGIN_NOT_ALLOWED);

        if (fetcher.Origins.Check(uri) != null)
            return Verdict(result, CorpusShardVerdict.OriginRefused, CorpusResultCodes.ORIGIN_NOT_LISTED);

        LanguageModel? model = null;
        if (task.Pipeline.IdentifyLanguage)
        {
            if (task.Pipeline.LanguageModelBlobId == Guid.Empty)
                return Verdict(result, CorpusShardVerdict.PipelineUnavailable, CorpusResultCodes.NO_LANGUAGE_MODEL);

            try
            {
                var modelPath = await ShardBlobTransfer.DownloadAsync(blobs, task.Pipeline.LanguageModelBlobId, LanguageModelLimits.MAX_BYTES, logger, cancellationToken);
                model = ShardLanguageModels.Load(modelPath, task.Pipeline.LanguageModelSha256 ?? "");
            }
            catch (FormatException)
            {
                // Bytes that match their pin and are no model this controller reads within its limits: the task's, on any node.
                return Verdict(result, CorpusShardVerdict.PipelineUnavailable, CorpusResultCodes.LANGUAGE_MODEL_REFUSED);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The model did not arrive here, or arrived with other bytes than its pin.
                return Fault(result, CorpusResultCodes.FAULT_LANGUAGE_MODEL, exception, logger);
            }
        }

        // The node's own ceiling, whoever wrote the task, and below it the size the shard was declared with.
        var cap = ShardLimits.FetchCap(task);
        var shardPath = Path.Combine(workDirectory, SHARD_FILE);
        var textPath = Path.Combine(workDirectory, TEXT_FILE);
        var signaturesPath = Path.Combine(workDirectory, SIGNATURES_FILE);

        var windowQueued = Stopwatch.StartNew();
        await WINDOW.WaitAsync(cancellationToken);
        var windowWait = windowQueued.Elapsed;
        ShardDiskReservation? room = null;
        try
        {
            beforeFetch?.Invoke();

            // The room the shard may take at the most, reserved before a byte is fetched and
            // held until its files are gone: the shard (the cap when it has no size) and the most
            // its products may be. It waits while the node's other shards hold the room.
            room = await ShardDiskReservations.ReserveAsync(workDirectory, ShardDisk.Needed(ShardBatchGuard.RoomBytes(task), task.Pipeline), cancellationToken);
            if (room == null)
                return Fault(result, CorpusResultCodes.FAULT_DISK_SPACE, null, logger);

            try
            {
                Directory.CreateDirectory(workDirectory);
            }
            catch (Exception exception) when (ShardLocalFaultException.IsLocal(exception))
            {
                return Fault(result, CorpusResultCodes.FAULT_DISK, exception, logger);
            }

            var fetchClock = Stopwatch.StartNew();
            ShardFetchOutcome fetch;
            try
            {
                fetch = await fetcher.FetchShardAsync(uri, task.AllowedOrigins ?? [], shardPath, cap, ticket == null ? null : ticket.Received, cancellationToken);
            }
            catch (ShardOversizedException)
            {
                // Past the cap, or past what the manifest declared for it: the shard's, on any node.
                return Verdict(result, CorpusShardVerdict.Oversized, ShardLimits.DeclaredBinds(task) ? CorpusResultCodes.OVERSIZED_DECLARED : CorpusResultCodes.OVERSIZED);
            }
            catch (ShardOriginRefusedException exception) when (exception.IsTheNodesResolver)
            {
                // A public origin's name resolved here to no public address: this node's resolver
                // said so, not the shard. Another node runs it.
                return Fault(result, CorpusResultCodes.FAULT_NETWORK, exception, logger);
            }
            catch (ShardOriginRefusedException exception)
            {
                // A redirect off the list or off what this machine fetches from.
                return Verdict(result, CorpusShardVerdict.OriginRefused, exception.Code);
            }
            catch (ShardFetchException exception) when (exception.Status is { } status && !exception.MayBeTheNodes)
            {
                // The origin answered, and its answer is not the shard.
                return Verdict(result, CorpusShardVerdict.FetchFailed, CorpusResultCodes.Http((int)status));
            }
            catch (ShardFetchException exception)
            {
                // Nobody answered this node, or a server error or a request to slow down outlasted
                // the retries: its link, a gateway or proxy in front of it, or the origin, and it
                // cannot tell which. Another node runs the shard.
                return Fault(result, CorpusResultCodes.FAULT_NETWORK, exception.InnerException ?? exception, logger);
            }
            catch (ShardLocalFaultException exception)
            {
                return Fault(result, exception.Code, exception.InnerException, logger);
            }

            fetchClock.Stop();
            ticket?.Fetched();

            if (!string.IsNullOrEmpty(task.Source.Sha256))
            {
                string actual;
                try
                {
                    actual = ShardLanguageModels.Hash(shardPath);
                }
                catch (Exception exception) when (ShardLocalFaultException.IsLocal(exception))
                {
                    return Fault(result, CorpusResultCodes.FAULT_DISK, exception, logger);
                }

                if (!string.Equals(actual, task.Source.Sha256, StringComparison.Ordinal))
                    return Verdict(result, CorpusShardVerdict.FetchFailed, CorpusResultCodes.HASH_MISMATCH);
            }

            var processingQueued = Stopwatch.StartNew();
            await PROCESSING.WaitAsync(cancellationToken);
            var processingWait = processingQueued.Elapsed;
            var processClock = Stopwatch.StartNew();
            ShardProduct product;
            try
            {
                product = await ShardWorker.RunAsync(() => Process(task, model, pdfStage ?? new ShardPdfStage(workDirectory), shardPath, textPath, signaturesPath, cancellationToken));
            }
            catch (WarcFormatException)
            {
                return Verdict(result, CorpusShardVerdict.Malformed, CorpusResultCodes.NOT_WARC);
            }
            catch (InvalidDataException)
            {
                return Verdict(result, CorpusShardVerdict.Malformed, CorpusResultCodes.BAD_COMPRESSION);
            }
            catch (ShardUnpackedOversizedException)
            {
                return Verdict(result, CorpusShardVerdict.Oversized, CorpusResultCodes.OVERSIZED_UNPACKED);
            }
            catch (ShardProductOversizedException)
            {
                // More documents or more text than a shard of its size may give: the shard's, on any node.
                return Verdict(result, CorpusShardVerdict.Oversized, CorpusResultCodes.OVERSIZED_PRODUCT);
            }
            finally
            {
                PROCESSING.Release();
            }

            processClock.Stop();

            // A body the origin did not frame can end early unnoticed; a shard that ends inside a
            // record after such a fetch is a fetch that failed, not a shorter shard.
            if (product.Truncated && !fetch.Framed)
                return Verdict(result, CorpusShardVerdict.FetchFailed, CorpusResultCodes.BODY_TRUNCATED);

            try
            {
                // Statistics and hashes only: the products stay in the task's folder and go with it.
                if (task.Pipeline.Output == CorpusOutputMode.Full)
                    result.TextBlobId = await ShardBlobTransfer.UploadAsync(blobs, textPath, logger, cancellationToken);

                if (ShardProcessor.KeepsSignatures(task.Pipeline))
                    result.SignaturesBlobId = await ShardBlobTransfer.UploadAsync(blobs, signaturesPath, logger, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.TextBlobId = Guid.Empty;
                result.SignaturesBlobId = Guid.Empty;
                return Fault(result, CorpusResultCodes.FAULT_UPLOAD, exception, logger);
            }

            // The signatures' identity, kept or not: integrity compares it across machines.
            if (ShardProcessor.Signs(task.Pipeline))
            {
                result.MinHash = MinHashParameters.Resolve(task.Pipeline.MinHash).ToData();
                result.SignaturesSha256 = product.SignaturesSha256;
            }

            result.Verdict = CorpusShardVerdict.Completed;
            result.ContentSha256 = product.ContentSha256;
            result.Error = product.Truncated ? CorpusResultCodes.NOTE_TRUNCATED : "";
            result.Stats = product.Stats.Build(
                fetch.Bytes,
                new FileInfo(textPath).Length,
                Milliseconds(fetchClock.Elapsed - fetch.Waited),
                Milliseconds(processClock.Elapsed),
                Milliseconds(windowWait + fetch.Waited + processingWait));

            return result;
        }
        finally
        {
            // The shard's place in the window is its place on the disk: the shard and its
            // products go before the place is given to the next one.
            TryDelete(shardPath);

            // An upload the job left running still reads its product: the products and their
            // room on the disk go when it ends, not before.
            var held = new[] { ShardBlobTransfer.HeldBy(textPath), ShardBlobTransfer.HeldBy(signaturesPath) }.OfType<Task>().ToArray();
            if (held.Length == 0)
            {
                TryDelete(textPath);
                TryDelete(signaturesPath);
                room?.Dispose();
            }
            else
            {
                var heldRoom = room;
                _ = Task.WhenAll(held).ContinueWith(_ =>
                {
                    TryDelete(textPath);
                    TryDelete(signaturesPath);
                    heldRoom?.Dispose();
                }, TaskScheduler.Default);
            }

            WINDOW.Release();
        }
    }

    private static ShardProduct Process(CorpusTaskData task, LanguageModel? model, ShardPdfStage pdfStage, string shardPath, string textPath, string signaturesPath, CancellationToken cancellationToken)
    {
        // A PDF's files live in the task's own scope (the client's temp storage), never elsewhere.
        var processor = new ShardProcessor(task.Pipeline!, model, pdfStage);

        using var shard = new FileStream(shardPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var text = new FileStream(textPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20);
        using var signatures = new FileStream(signaturesPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16);

        // On the shard's own thread (ShardWorker): below normal priority, the PDF sandbox's guest
        // code included, which runs on the thread that calls it when that thread can hold it.
        return processor.Process(shard, task.Source!.Format, text, signatures, cancellationToken);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // It goes with the task's folder.
        }
    }

    private static int Milliseconds(TimeSpan span)
    {
        return (int)Math.Clamp(span.TotalMilliseconds, 0, int.MaxValue);
    }

    /// <summary>The code of a fault nothing more specific caught.</summary>
    private static string FaultCode(Exception exception)
    {
        return exception switch
        {
            Pdf.PdfSandboxException sandbox => sandbox.Code,
            ShardLocalFaultException local => local.Code,
            OutOfMemoryException or InsufficientMemoryException => CorpusResultCodes.FAULT_MEMORY,
            _ when ShardLocalFaultException.IsLocal(exception) => CorpusResultCodes.FAULT_DISK,
            _ => CorpusResultCodes.FAULT_INTERNAL
        };
    }

    private static CorpusResultData Verdict(CorpusResultData result, CorpusShardVerdict verdict, string code)
    {
        result.Verdict = verdict;
        result.Error = code;
        return result;
    }

    /// <summary>
    /// The shard goes back unanswered. The result carries the fault's code and nothing else; what
    /// the fault was goes to the node's own log, as the kind of the failure and its system code,
    /// without its message (a message of the file system or the HTTP stack names paths and hosts).
    /// </summary>
    private static CorpusResultData Fault(CorpusResultData result, string code, Exception? exception, ILogger? logger)
    {
        logger?.LogWarning("Corpus.ProcessPackage, shard {Index}: a fault of this node, {Code} ({Kind}, 0x{HResult:X8}); the shard goes back unanswered.",
            result.TaskIndex, code, exception?.GetType().Name ?? "none", exception?.HResult ?? 0);

        result.Verdict = CorpusShardVerdict.NodeFault;
        result.Error = CorpusResultCodes.IsFault(code) ? code : CorpusResultCodes.FAULT_INTERNAL;
        result.TextBlobId = Guid.Empty;
        result.SignaturesBlobId = Guid.Empty;
        result.ContentSha256 = "";
        result.SignaturesSha256 = "";
        result.MinHash = null;
        result.Stats = null;
        return result;
    }

    #endregion
}
