using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifyExecuteBatch : WitActivityAdapterFunction<WitActivityVerifyExecuteBatch>
{
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
        var result = await Task.Run(() => VerifyBatchExecutor.Execute(VerifySandboxHost.Instance, runtimesRoot, batch));

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    protected override double EstimateWork(WitActivityVerifyExecuteBatch activity, IWitVariablesCollection pool)
    {
        // O(1), never opens a source: work ≈ task count until a throughput benchmark supplies a real rate.
        return pool.TryGetValue(activity.Batch, out VerifyTaskBatchData? batch) && batch != null
            ? Math.Max(1, batch.Tasks.Count)
            : 1.0;
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
