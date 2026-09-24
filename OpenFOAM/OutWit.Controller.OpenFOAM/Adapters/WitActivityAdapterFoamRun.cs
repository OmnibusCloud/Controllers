using Microsoft.Extensions.Logging;
using OutWit.Controller.OpenFOAM.Activities;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Adapters;

internal sealed class WitActivityAdapterFoamRun : WitActivityAdapterFunction<WitActivityFoamRun>
{
    #region Constructors

    public WitActivityAdapterFoamRun(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityFoamRun activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Task, out FoamTaskData? task) || task == null)
            throw new InvalidOperationException("Failed to get parameter 'Task'.");

        var kit = FoamKitResolver.Resolve(GetType().Assembly.Location, Logger)
            ?? throw new InvalidOperationException(
                "The OpenFOAM kit was not found in the controller module. Ensure the module includes the kit for this platform.");

        // The job's cancellation must reach the RUNNING solver: without the
        // token a cancelled overnight sweep keeps every node's in-flight case
        // running to completion, and cancel only takes effect between activities.
        var cancellation = ProcessingManager.CancellationToken(status.JobId);

        var session = new FoamCaseSession(kit, BlobService, Logger);
        var result = await session.RunAsync(task, cancellation);

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    public override async Task<IWitBenchmarkResult> RunBenchmark(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        var kit = FoamKitResolver.Resolve(GetType().Assembly.Location, Logger);
        if (kit == null)
        {
            // No kit for this platform: the node reports the default
            // (unranked) score instead of failing registration.
            Logger.LogWarning("Foam.Run benchmark: OpenFOAM kit not found - reporting the default score.");
            return WitBenchmarkResult.Default;
        }

        var result = await FoamBenchmark.MeasureAsync(kit, options, cancellationToken);

        Logger.LogInformation(
            "Foam.Run benchmark: {Rate:F4} {Unit} (median of {Runs} reference runs: {RunTimes} s; warm-up {Warmup} s)",
            result.Rate, result.Unit, result.Iterations, result.Custom?["runs_s"], result.Custom?["warmup_s"]);

        return result;
    }

    protected override double EstimateWork(WitActivityFoamRun activity, IWitVariablesCollection pool)
    {
        if (!pool.TryGetValue(activity.Task, out FoamTaskData? task) || task == null)
            return FoamWorkEstimate.UNKNOWN;

        return FoamWorkEstimate.Estimate(task);
    }

    #endregion

    #region Parsing

    protected override WitActivityFoamRun CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference task)
                throw new ArgumentException("Parameter 'Task' must be a variable reference.");

            return new WitActivityFoamRun
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

    #endregion
}
