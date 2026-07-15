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

internal sealed class WitActivityAdapterVerifyExecute : WitActivityAdapterFunction<WitActivityVerifyExecute>
{
    #region Constructors

    public WitActivityAdapterVerifyExecute(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyExecute activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Task, out VerifyTaskData? task) || task == null)
            throw new InvalidOperationException("Failed to get parameter 'Task'.");

        // A one-task batch reuses the batch executor's runtime resolution + verdict mapping.
        var batch = new VerifyTaskBatchData { RuntimeId = task.RuntimeId, Tasks = [task] };
        var runtimesRoot = VerifyRuntimeLocator.Locate();
        var result = await Task.Run(() => VerifyBatchExecutor.Execute(VerifySandboxHost.Instance, runtimesRoot, batch));

        if (!pool.TrySetValue(activity.ReturnReference, result.Results[0]))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyExecute CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference task)
                throw new ArgumentException("Parameter 'Task' must be a variable reference.");

            return new WitActivityVerifyExecute
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
}
