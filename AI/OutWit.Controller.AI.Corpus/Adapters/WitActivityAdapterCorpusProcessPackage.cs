using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusProcessPackage : WitActivityAdapterFunction<WitActivityCorpusProcessPackage>
{
    #region Constants

    /// <summary>Work estimate per shard when nothing gives its size: one typical Common Crawl WET file (CC-MAIN-2026-43: 60-65 MB).</summary>
    private const double DEFAULT_SHARD_MEGABYTES = 64;

    private const double BYTES_PER_MIB = 1024.0 * 1024.0;

    private static readonly Lazy<ShardFetcher> FETCHER = new(() => new ShardFetcher(ShardFetchPolicy.Default, ShardOriginPolicy.Machine));

    #endregion

    #region Fields

    private static ShardFetcher? m_fetcher;

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusProcessPackage(IWitProcessingManager processingManager, IWitBlobService blobService, IWitTempStorage tempStorage, ILogger logger)
        : base(processingManager, CorpusLogger.Wrap(logger))
    {
        BlobService = blobService;
        TempStorage = tempStorage;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusProcessPackage activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Package, out CorpusTaskPackageData? package) || package == null)
            throw new InvalidOperationException("Failed to get parameter 'Package'.");

        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var jobId = status.JobId;

        // The scope is unique to this execution, not to the package: a later round of the job may
        // bring the same shards to this node again. A node that cannot make it refuses the batch.
        string scope;
        try
        {
            scope = ShardBatchGuard.CreateScope(TempStorage, $"corpus-{jobId:N}-{Guid.NewGuid():N}");
        }
        catch (ShardBatchRefusedException exception)
        {
            Logger.LogWarning("Corpus.ProcessPackage: the batch is refused ({Code}).", exception.Code);
            throw;
        }

        try
        {
            // What this node can know before any work, from the package's reference alone: its
            // version against the job's, its PDF sandbox when a shard needs one, room on its disk
            // for the largest shard. A node that fails one refuses the batch here, with nothing
            // fetched, and the platform moves the work.
            ShardBatchGuard.ThrowIfRefused(jobId, package, scope);
        }
        catch (ShardBatchRefusedException exception)
        {
            Logger.LogWarning("Corpus.ProcessPackage: the batch is refused ({Code}).", exception.Code);
            TryDeleteScope(scope);
            throw;
        }

        CorpusResultPackageData answer;
        try
        {
            answer = await CorpusPackageRunner.RunAsync(package, Fetcher, BlobService, scope, jobId, CorpusActivityProgressReporter.Create(ProcessingManager, jobId), Logger, () => ShardBatchGuard.ThrowIfJobRefused(jobId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ShardBatchRefusedException)
        {
            // Whatever was not foreseen is this node's: the package goes back as a fault, in a
            // fixed word. What a node throws is the job's message for the lab.
            Logger.LogWarning("Corpus.ProcessPackage: a fault of this node ({Kind}, 0x{HResult:X8}); the package goes back unanswered.", exception.GetType().Name, exception.HResult);
            answer = CorpusPackageRunner.Fault(package, CorpusResultCodes.FAULT_INTERNAL);
        }
        finally
        {
            // Not on the answer's path: a delete can wait on the node's file system far longer
            // than the work took (ShardCleanup). The shards and their products are gone already.
            ShardCleanup.Schedule(() => TryDeleteScope(scope));
        }

        if (answer.Fault.Length == 0)
            Logger.LogInformation("Corpus.ProcessPackage: {Shards} shard(s) answered.", package.ShardCount);
        else
            Logger.LogWarning("Corpus.ProcessPackage: the package of {Shards} shard(s) goes back unanswered ({Fault}).", package.ShardCount, answer.Fault);

        if (!pool.TrySetValue(activity.ReturnReference, answer))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    private void TryDeleteScope(string scope)
    {
        try
        {
            TempStorage.DeleteScope(scope);
        }
        catch (Exception exception)
        {
            Logger.LogWarning("Corpus.ProcessPackage: the work directory could not be deleted ({Kind}).", exception.GetType().Name);
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
                "Corpus.ProcessPackage benchmark: {Rate:F2} {Unit} at degree {Degree} (median of {Runs} runs: {RunTimes} s; warm-up {Warmup} s)",
                result.Rate, result.Unit, result.Custom?["degree"], result.Iterations, result.Custom?["runs_s"], result.Custom?["warmup_s"]);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Corpus.ProcessPackage benchmark failed - reporting the default score.");
            return WitBenchmarkResult.Default;
        }
    }

    protected override double EstimateWork(WitActivityCorpusProcessPackage activity, IWitVariablesCollection pool)
    {
        // O(1), from the reference: the megabytes of the package's shards on the wire, a typical
        // shard for each one whose size is not known.
        if (pool.TryGetValue(activity.Package, out CorpusTaskPackageData? package) && package != null)
            return Work(package);

        return DEFAULT_SHARD_MEGABYTES;
    }

    /// <summary>The work of a package as the platform's allocator weighs it: megabytes of shard, at least one.</summary>
    /// <param name="package">The package's reference.</param>
    /// <returns>The work units.</returns>
    internal static double Work(CorpusTaskPackageData package)
    {
        return Math.Max(1, Math.Max(0, package.ShardBytes) / BYTES_PER_MIB + Math.Max(0, package.UnsizedShards) * DEFAULT_SHARD_MEGABYTES);
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusProcessPackage CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference package)
                throw new ArgumentException("Parameter 'Package' must be a variable reference.");

            return new WitActivityCorpusProcessPackage
            {
                Package = package
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

    /// <summary>
    /// The fetcher of this process: one node's, with a node's two downloads at once. A test rig
    /// that plays several nodes inside one process may give it the downloads of all of them
    /// (null: back to the node's own).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    internal static ShardFetcher Fetcher
    {
        get => Volatile.Read(ref m_fetcher) ?? FETCHER.Value;
        set => Volatile.Write(ref m_fetcher, value);
    }

    private IWitBlobService BlobService { get; }

    private IWitTempStorage TempStorage { get; }

    #endregion
}
