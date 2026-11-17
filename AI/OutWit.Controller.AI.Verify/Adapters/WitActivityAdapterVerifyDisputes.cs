using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifyDisputes : WitActivityAdapterFunction<WitActivityVerifyDisputes>
{
    #region Constructors

    public WitActivityAdapterVerifyDisputes(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyDisputes activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var issued = VerifyAdapterTools.TaskPackages(pool, activity.Packages, "Packages");
        var returned = VerifyAdapterTools.ResultPackages(pool, activity.Results, "Results");

        // Executions that disagree are not chosen between: the task runs three more times.
        var main = await VerifySettlement.ReadMainAsync(issued, returned, BlobService);
        var batches = await VerifySettlement.DisputeBatchesAsync(main, BlobService);

        Logger.LogInformation("Verify.Disputes: {Tasks} task(s) whose executions disagree run three more times", batches.Sum(batch => batch.Tasks.Count));

        var packages = batches.Count == 0
            ? []
            : await VerifyTaskPackages.PublishAsync(batches, BlobService, Logger, "Verify.Disputes");

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyDisputes CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            return new WitActivityVerifyDisputes
            {
                Packages = VerifyAdapterTools.Reference(parameters, 0, "Packages"),
                Results = VerifyAdapterTools.Reference(parameters, 1, "Results")
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
