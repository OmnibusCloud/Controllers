using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifyExecutePackage : WitActivityAdapterFunction<WitActivityVerifyExecutePackage>
{
    #region Constants

    /// <summary>
    /// What a failure of the node's own says to whoever reads the job's status: fixed words. What
    /// happened is in the node's log; an exception's own text can name a folder of the node.
    /// </summary>
    private const string NODE_FAILURE = "Verify: this node could not run the package for a reason of its own (its log says which)";

    #endregion

    #region Fields

    /// <summary>
    /// One package executes at a time in this process: a batch already takes the node's cores.
    /// The other packages of the share wait here with their blobs downloaded.
    /// </summary>
    private static readonly SemaphoreSlim EXECUTION = new(1, 1);

    #endregion

    #region Constructors

    public WitActivityAdapterVerifyExecutePackage(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, VerifyLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;

        // Before the sandbox is first used (a benchmark may be that use): its working files
        // belong in the host's temp storage, the client's own folder on a contributor's machine.
        VerifySandboxHost.UseTempStorage(services.GetService<IWitTempStorage>());
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyExecutePackage activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Package, out VerifyTaskPackageData? package) || package == null)
            throw new InvalidOperationException("Failed to get parameter 'Package'.");

        // The job's token: a cancelled job leaves the queue at once and stops a running batch at
        // the next task boundary instead of burning the rest of the share on the node.
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);

        VerifyResultPackageData result;
        try
        {
            VerifyPackageRunner.EnsureAcceptable(package);
            result = await Run(package, status.JobId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VerifyBatchRefusedException refused)
        {
            // This node cannot do the package and knew it before it ran any of it: the platform
            // gives it to another node. The reason travels; where on the node it lies does not.
            Logger.LogWarning("Verify: this node refuses a package: {Reason} {Detail}", refused.Message, refused.NodeDetail ?? "");
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Verify: a package failed on this node");
            throw new InvalidOperationException(NODE_FAILURE);
        }

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    private async Task<VerifyResultPackageData> Run(VerifyTaskPackageData package, Guid jobId, CancellationToken cancellationToken)
    {
        var progress = VerifyDeliveryProgress.Enter(jobId, package, VerifyActivityProgressReporter.Create(ProcessingManager, jobId), Logger);
        string? resultsPath = null;

        try
        {
            var path = await VerifyPackageTransfer.FetchAsync(BlobService, package, Logger, cancellationToken);
            progress.Received(package);

            VerifyWrittenResults written;

            await EXECUTION.WaitAsync(cancellationToken);
            try
            {
                var batches = await VerifyPackageTransfer.ReadTasksAsync(BlobService, package, path, Logger, cancellationToken);
                var runtimesRoot = VerifyRuntimeLocator.Locate();

                written = await Task.Run(
                    () => VerifyPackageRunner.Run(package, batches, () => VerifySandboxHost.Instance, runtimesRoot, progress.TaskDone, Logger, cancellationToken),
                    cancellationToken);
                resultsPath = written.Path;
            }
            finally
            {
                EXECUTION.Release();
            }

            return await VerifyPackageTransfer.UploadResultsAsync(BlobService, written.Path, package.Ticket, written.Results, written.Batches, Logger, cancellationToken);
        }
        finally
        {
            VerifyPackageRunner.TryDelete(resultsPath);
            progress.Leave();
        }
    }

    /// <summary>
    /// Measures this node's throughput on the reference workload; a node without runtimes, with
    /// one that does not pass its pin, or with a failing sandbox reports the default (unranked)
    /// score: it would refuse the packages it was given, so it should not be given a share.
    /// </summary>
    /// <param name="options">The engine's benchmark options.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The rate in tasks per second at the node's degree.</returns>
    public override async Task<IWitBenchmarkResult> RunBenchmark(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        var runtimesRoot = VerifyRuntimeLocator.Locate();
        if (runtimesRoot == null)
        {
            // No language runtimes on this node: report the default (unranked) score instead of
            // failing the registration, the CalculiX precedent for a missing solver.
            Logger.LogWarning("Verify.ExecutePackage benchmark: language runtimes not found - reporting the default score.");
            return WitBenchmarkResult.Default;
        }

        // The reference workload runs two runtimes; a share may bring any of them.
        foreach (var runtimeId in VerifyRuntimeCatalog.KnownIds)
        {
            if (VerifyRuntimeCatalog.Resolve(runtimesRoot, runtimeId, out var reason, out var detail) != null)
                continue;

            Logger.LogWarning("Verify.ExecutePackage benchmark: runtime {Runtime} cannot be used on this node ({Reason} {Detail}) - reporting the default score.", runtimeId, reason, detail ?? "");
            return WitBenchmarkResult.Default;
        }

        try
        {
            var result = await Task.Run(() => VerifyBenchmark.Measure(VerifySandboxHost.Instance, runtimesRoot, options, cancellationToken), cancellationToken);

            Logger.LogInformation(
                "Verify.ExecutePackage benchmark: {Rate:F2} {Unit} at degree {Degree} (median of {Runs} runs: {RunTimes} s; warm-up {Warmup} s)",
                result.Rate, result.Unit, result.Custom?["degree"], result.Iterations, result.Custom?["runs_s"], result.Custom?["warmup_s"]);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A sandbox that cannot run the reference workload must not score; the node stays
            // registered, ranked last, with the reason in its log.
            Logger.LogWarning(exception, "Verify.ExecutePackage benchmark failed - reporting the default score.");
            return WitBenchmarkResult.Default;
        }
    }

    protected override double EstimateWork(WitActivityVerifyExecutePackage activity, IWitVariablesCollection pool)
    {
        // O(1), never opens the blob: work ≈ task count, the unit the benchmark's rate is in.
        return pool.TryGetValue(activity.Package, out VerifyTaskPackageData? package) && package != null
            ? Math.Max(1, package.TaskCount)
            : 1.0;
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyExecutePackage CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference package)
                throw new ArgumentException("Parameter 'Package' must be a variable reference.");

            return new WitActivityVerifyExecutePackage
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

    private IWitBlobService BlobService { get; }

    #endregion
}
