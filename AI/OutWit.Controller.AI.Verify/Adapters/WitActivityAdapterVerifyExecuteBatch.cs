using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifyExecuteBatch : WitActivityAdapterFunction<WitActivityVerifyExecuteBatch>
{
    #region Constants

    /// <summary>Per-task progress reaches the host at most this often (the final report always does).</summary>
    private static readonly TimeSpan PROGRESS_INTERVAL = TimeSpan.FromMilliseconds(500);

    #endregion

    #region Constructors

    public WitActivityAdapterVerifyExecuteBatch(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyExecuteBatch activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Batch, out VerifyTaskBatchData? batch) || batch == null)
            throw new InvalidOperationException("Failed to get parameter 'Batch'.");

        var runtimesRoot = VerifyRuntimeLocator.Locate();
        var progress = CreateProgress(status.JobId, batch.Tasks.Count);
        var nodeId = status.EngineId;

        // The job's token: a cancelled job stops at the next task boundary instead of burning
        // the rest of the batch on the node.
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var result = await Task.Run(() => VerifyBatchExecutor.Execute(VerifySandboxHost.Instance, runtimesRoot, batch, nodeId, progress, cancellationToken), cancellationToken);

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
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
            Logger.LogWarning("Verify.ExecuteBatch benchmark: language runtimes not found - reporting the default score.");
            return WitBenchmarkResult.Default;
        }

        try
        {
            var result = await Task.Run(() => VerifyBenchmark.Measure(VerifySandboxHost.Instance, runtimesRoot, options, cancellationToken), cancellationToken);

            Logger.LogInformation(
                "Verify.ExecuteBatch benchmark: {Rate:F2} {Unit} at degree {Degree} (median of {Runs} runs: {RunTimes} s; warm-up {Warmup} s)",
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
            Logger.LogWarning(exception, "Verify.ExecuteBatch benchmark failed - reporting the default score.");
            return WitBenchmarkResult.Default;
        }
    }

    protected override double EstimateWork(WitActivityVerifyExecuteBatch activity, IWitVariablesCollection pool)
    {
        // O(1), never opens a source: work ≈ task count until a throughput benchmark supplies a real rate.
        return pool.TryGetValue(activity.Batch, out VerifyTaskBatchData? batch) && batch != null
            ? Math.Max(1, batch.Tasks.Count)
            : 1.0;
    }

    #endregion

    #region Tools

    /// <summary>
    /// Per-task completion → the host's activity progress sink, throttled to one report per
    /// <see cref="PROGRESS_INTERVAL"/> plus the final one; null when the host has no sink.
    /// </summary>
    private Action<int, int>? CreateProgress(Guid jobId, int total)
    {
        var report = VerifyActivityProgressReporter.Create(ProcessingManager, jobId);
        if (report == null || total <= 0)
            return null;

        var gate = new object();
        var since = Stopwatch.StartNew();
        return (done, count) =>
        {
            lock (gate)
            {
                if (done < count && since.Elapsed < PROGRESS_INTERVAL)
                    return;
                since.Restart();
            }

            report((double)done / count, $"{done}/{count} tasks");
        };
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyExecuteBatch CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference batch)
                throw new ArgumentException("Parameter 'Batch' must be a variable reference.");

            return new WitActivityVerifyExecuteBatch
            {
                Batch = batch
            };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion
}
