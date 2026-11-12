using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Warc;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// One task on a node, start to finish: the allowlist re-check against the task and the
/// machine's own policy, the pinned model, the capped fetch, the pipeline, the uploads. Every way a shard can fail is a verdict in the result,
/// not an exception — the job completes and the report says what happened to each shard;
/// only cancellation propagates.
/// </summary>
public static class ShardTaskRunner
{
    #region Constants

    private const string SHARD_FILE = "shard.gz";
    private const string TEXT_FILE = "text.jsonl.zst";
    private const string SIGNATURES_FILE = "signatures.bin";

    #endregion

    #region Fields

    /// <summary>Shards processing at once in this process: the node's degree (cores, memory). The client starts every task of a parallel batch together; the rest wait here, fetched and ready.</summary>
    private static readonly SemaphoreSlim PROCESSING = new(ShardDegree.ForCurrentMachine());

    #endregion

    #region Functions

    /// <summary>Runs one task.</summary>
    /// <param name="task">The task.</param>
    /// <param name="fetcher">Fetches the shard.</param>
    /// <param name="blobs">Resolves the model blob and receives the products.</param>
    /// <param name="workDirectory">A directory of this task's own for the shard and the products; the caller deletes it.</param>
    /// <param name="cancellationToken">Cancels the task.</param>
    /// <returns>The result, verdict included.</returns>
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
    /// <param name="logger">Receives a warning per failed blob transfer attempt; null for none.</param>
    /// <param name="cancellationToken">Cancels the task.</param>
    /// <returns>The result, verdict included.</returns>
    public static async Task<CorpusResultData> RunAsync(CorpusTaskData task, ShardFetcher fetcher, IWitBlobService blobs, string workDirectory, ShardShareTicket? ticket, ILogger? logger, CancellationToken cancellationToken)
    {
        var result = new CorpusResultData
        {
            TaskIndex = task.TaskIndex,
            SourceUrl = task.Source?.Url ?? "",
            ControllerVersion = ControllerBuildInfo.VERSION
        };

        if (task.Source == null || task.Pipeline == null)
            return Fail(result, CorpusShardVerdict.Failed, "The task carries no source or no pipeline.");

        // The task's list AND this machine's policy: the task is written by the submitter and
        // cannot widen what the machine's owner agreed to fetch from.
        var refusal = ShardOrigin.Check(task.Source.Url, task.AllowedOrigins, fetcher.Origins, out var uri);
        if (refusal != null || uri == null)
            return Fail(result, CorpusShardVerdict.OriginRefused, refusal ?? "The source URL is not fetchable.");

        LanguageModel? model = null;
        if (task.Pipeline.IdentifyLanguage)
        {
            if (task.Pipeline.LanguageModelBlobId == Guid.Empty)
                return Fail(result, CorpusShardVerdict.PipelineUnavailable, "The pipeline identifies languages but names no language model.");

            try
            {
                var modelPath = await ShardBlobTransfer.DownloadAsync(blobs, task.Pipeline.LanguageModelBlobId, logger, cancellationToken);
                model = ShardLanguageModels.Load(modelPath, task.Pipeline.LanguageModelSha256);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Fail(result, CorpusShardVerdict.PipelineUnavailable, $"The language model could not be loaded: {exception.Message}");
            }
        }

        Directory.CreateDirectory(workDirectory);
        var shardPath = Path.Combine(workDirectory, SHARD_FILE);
        var textPath = Path.Combine(workDirectory, TEXT_FILE);
        var signaturesPath = Path.Combine(workDirectory, SIGNATURES_FILE);

        var fetchClock = Stopwatch.StartNew();
        long fetched;
        TimeSpan fetchWait;
        try
        {
            (fetched, fetchWait) = await fetcher.FetchMeasuredAsync(uri, task.AllowedOrigins, shardPath, task.MaxShardBytes, ticket == null ? null : ticket.Received, cancellationToken);
        }
        catch (ShardOversizedException exception)
        {
            return Fail(result, CorpusShardVerdict.Oversized, exception.Message);
        }
        catch (ShardOriginRefusedException exception)
        {
            // A redirect off the list, or a name that resolves into the machine's own network.
            return Fail(result, CorpusShardVerdict.OriginRefused, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return Fail(result, CorpusShardVerdict.FetchFailed, exception.Message);
        }

        fetchClock.Stop();
        ticket?.Fetched();

        if (task.Source.Sha256.Length > 0)
        {
            var actual = ShardLanguageModels.Hash(shardPath);
            if (!string.Equals(actual, task.Source.Sha256, StringComparison.Ordinal))
                return Fail(result, CorpusShardVerdict.FetchFailed, $"The fetched bytes hash to {actual}, not the published {task.Source.Sha256}.");
        }

        var processingQueued = Stopwatch.StartNew();
        await PROCESSING.WaitAsync(cancellationToken);
        var processingWait = processingQueued.Elapsed;
        var processClock = Stopwatch.StartNew();
        ShardProduct product;
        try
        {
            product = await Task.Run(() => Process(task, model, shardPath, textPath, signaturesPath, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is WarcFormatException or InvalidDataException)
        {
            return Fail(result, CorpusShardVerdict.Malformed, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Fail(result, CorpusShardVerdict.Failed, exception.Message);
        }
        finally
        {
            PROCESSING.Release();
        }

        processClock.Stop();

        result.TextBlobId = await ShardBlobTransfer.UploadAsync(blobs, textPath, logger, cancellationToken);
        if (task.Pipeline.Sign)
            result.SignaturesBlobId = await ShardBlobTransfer.UploadAsync(blobs, signaturesPath, logger, cancellationToken);

        result.Verdict = CorpusShardVerdict.Completed;
        result.ContentSha256 = product.ContentSha256;
        result.Stats = product.Stats.Build(
            fetched,
            new FileInfo(textPath).Length,
            Milliseconds(fetchClock.Elapsed - fetchWait),
            Milliseconds(processClock.Elapsed),
            Milliseconds(fetchWait + processingWait));

        if (product.Truncated || product.SkippedOversized > 0)
            result.Error = $"The shard ended mid-record: {product.Truncated}; records skipped as oversized: {product.SkippedOversized}.";

        return result;
    }

    private static ShardProduct Process(CorpusTaskData task, LanguageModel? model, string shardPath, string textPath, string signaturesPath, CancellationToken cancellationToken)
    {
        var processor = new ShardProcessor(task.Pipeline!, model);

        using var shard = new FileStream(shardPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var text = new FileStream(textPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20);
        using var signatures = new FileStream(signaturesPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16);

        // A worker is often a desktop: the minutes of CPU a shard takes run below the owner's
        // foreground work, the way the solvers run their processes below normal priority.
        var thread = Thread.CurrentThread;
        var priority = thread.Priority;
        try
        {
            thread.Priority = ThreadPriority.BelowNormal;
            return processor.Process(shard, task.Source!.Format, text, signatures, cancellationToken);
        }
        finally
        {
            thread.Priority = priority;
        }
    }

    private static int Milliseconds(TimeSpan span)
    {
        return (int)Math.Clamp(span.TotalMilliseconds, 0, int.MaxValue);
    }

    private static CorpusResultData Fail(CorpusResultData result, CorpusShardVerdict verdict, string error)
    {
        result.Verdict = verdict;
        result.Error = error;
        return result;
    }

    #endregion
}
