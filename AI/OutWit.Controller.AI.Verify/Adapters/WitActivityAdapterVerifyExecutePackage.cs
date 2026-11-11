using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Delivery;
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
    #region Fields

    /// <summary>
    /// One package executes at a time in this process: a batch already takes the node's cores.
    /// The other packages of the share wait here with their blobs downloaded.
    /// </summary>
    private static readonly SemaphoreSlim EXECUTION = new(1, 1);

    #endregion

    #region Constructors

    public WitActivityAdapterVerifyExecutePackage(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyExecutePackage activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Package, out VerifyTaskPackageData? package) || package == null)
            throw new InvalidOperationException("Failed to get parameter 'Package'.");

        if (package.TasksBlobId == Guid.Empty)
            throw new InvalidOperationException("The package names no task blob.");

        // The job's token: a cancelled job leaves the queue at once and stops a running batch at
        // the next task boundary instead of burning the rest of the share on the node.
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var progress = VerifyDeliveryProgress.Enter(status.JobId, package, VerifyActivityProgressReporter.Create(ProcessingManager, status.JobId), Logger);

        try
        {
            var path = await VerifyPackageTransfer.FetchAsync(BlobService, package, Logger, cancellationToken);
            progress.Received(package);

            List<VerifyResultBatchData> results;

            await EXECUTION.WaitAsync(cancellationToken);
            try
            {
                var batches = await VerifyPackageTransfer.ReadTasksAsync(BlobService, package, path, Logger, cancellationToken);
                results = await Task.Run(() => Execute(batches, progress, cancellationToken), cancellationToken);
            }
            finally
            {
                EXECUTION.Release();
            }

            var result = await VerifyPackageTransfer.UploadResultsAsync(BlobService, results, Logger, cancellationToken);

            if (!pool.TrySetValue(activity.ReturnReference, result))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
        }
        finally
        {
            progress.Leave();
        }
    }

    /// <summary>Measures this node's throughput on the reference workload; a node without runtimes or with a failing sandbox reports the default (unranked) score.</summary>
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

    #region Tools

    private static List<VerifyResultBatchData> Execute(IReadOnlyList<VerifyTaskBatchData> batches, VerifyDeliveryProgress progress, CancellationToken cancellationToken)
    {
        var runtimesRoot = VerifyRuntimeLocator.Locate();
        var results = new List<VerifyResultBatchData>(batches.Count);

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(VerifyBatchExecutor.Execute(VerifySandboxHost.Instance, runtimesRoot, batch, (_, _) => progress.TaskDone(), cancellationToken));
        }

        return results;
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
