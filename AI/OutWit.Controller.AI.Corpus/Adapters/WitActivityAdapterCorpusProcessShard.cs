using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusProcessShard : WitActivityAdapterFunction<WitActivityCorpusProcessShard>
{
    #region Constants

    /// <summary>Work estimate per shard when nothing gives its size: one typical Common Crawl WET file (CC-MAIN-2026-43: 60-65 MB).</summary>
    private const double DEFAULT_SHARD_MEGABYTES = 64;

    private static readonly Lazy<ShardFetcher> FETCHER = new(() => new ShardFetcher(ShardFetcher.CreateClient(), ShardFetchPolicy.Default));

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusProcessShard(IWitProcessingManager processingManager, IWitBlobService blobService, IWitTempStorage tempStorage, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        TempStorage = tempStorage;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusProcessShard activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Task, out CorpusTaskData? task) || task == null)
            throw new InvalidOperationException("Failed to get parameter 'Task'.");

        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var scope = TempStorage.CreateScope($"corpus-{status.JobId:N}-{task.TaskIndex}");

        try
        {
            var result = await ShardTaskRunner.RunAsync(task, FETCHER.Value, BlobService, scope, cancellationToken);

            if (result.Verdict == CorpusShardVerdict.Completed)
                Logger.LogInformation("Corpus.ProcessShard {Index}: {Out}/{In} documents kept, {Bytes} bytes in, {Fetch} ms fetch, {Process} ms process",
                    task.TaskIndex, result.Stats?.DocumentsOut, result.Stats?.RecordsIn, result.Stats?.BytesFetched, result.Stats?.FetchMs, result.Stats?.ProcessMs);
            else
                Logger.LogWarning("Corpus.ProcessShard {Index}: {Verdict} - {Error}", task.TaskIndex, result.Verdict, result.Error);

            if (!pool.TrySetValue(activity.ReturnReference, result))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
        }
        finally
        {
            try
            {
                TempStorage.DeleteScope(scope);
            }
            catch (Exception exception)
            {
                Logger.LogWarning(exception, "Corpus.ProcessShard {Index}: the work directory could not be deleted.", task.TaskIndex);
            }
        }
    }

    /// <summary>Measures this node on the reference shard at its shard degree; a node whose pipeline computes a different product reports the default (unranked) score.</summary>
    /// <param name="options">The engine's benchmark options.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The rate in megabytes of real Common Crawl WET per second.</returns>
    public override async Task<IWitBenchmarkResult> RunBenchmark(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Task.Run(() => CorpusBenchmark.Measure(options, cancellationToken), cancellationToken);

            Logger.LogInformation(
                "Corpus.ProcessShard benchmark: {Rate:F2} {Unit} at degree {Degree} (median of {Runs} runs: {RunTimes} s; warm-up {Warmup} s)",
                result.Rate, result.Unit, result.Custom?["degree"], result.Iterations, result.Custom?["runs_s"], result.Custom?["warmup_s"]);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Corpus.ProcessShard benchmark failed - reporting the default score.");
            return WitBenchmarkResult.Default;
        }
    }

    protected override double EstimateWork(WitActivityCorpusProcessShard activity, IWitVariablesCollection pool)
    {
        // O(1): megabytes on the wire when the manifest says, else a typical shard.
        if (pool.TryGetValue(activity.Task, out CorpusTaskData? task) && task?.Source is { SizeBytes: > 0 } source)
            return Math.Max(1, source.SizeBytes / (1024.0 * 1024.0));

        return DEFAULT_SHARD_MEGABYTES;
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusProcessShard CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference task)
                throw new ArgumentException("Parameter 'Task' must be a variable reference.");

            return new WitActivityCorpusProcessShard
            {
                Task = task
            };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    private IWitTempStorage TempStorage { get; }

    #endregion
}
