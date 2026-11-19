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
        : base(processingManager, VerifyLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyDisputes activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var issued = VerifyAdapterTools.TaskPackages(pool, activity.Packages, "Packages");
        var returned = VerifyAdapterTools.ResultPackages(pool, activity.Results, "Results");

        // Executions that disagree are not chosen between: the task runs three more times. A
        // script that hands over its repair round ran it under the sampling of the first
        // fan-out, so the executions of a repaired task can disagree as well.
        var main = await VerifySettlement.ReadMainAsync(issued, returned, BlobService);
        var repair = activity.Repairs == null
            ? null
            : await VerifySettlement.ReadRepairAsync(VerifyAdapterTools.TaskPackages(pool, activity.Repairs, "Repairs"),
                VerifyAdapterTools.ResultPackages(pool, activity.Repaired, "Repaired"), sampled: true, BlobService);
        var packages = await VerifySettlement.PublishDisputesAsync(main, repair, BlobService);

        Logger.LogInformation("Verify.Disputes: {Tasks} task(s) whose executions disagree run three more times in {Packages} package(s)",
            packages.Sum(package => (long)package.TaskCount), packages.Count);

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyDisputes CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2 && parameters.Length != 4)
                throw new ArgumentException($"Expected 2 or 4 parameter(s), got {parameters.Length}.");

            return new WitActivityVerifyDisputes
            {
                Packages = VerifyAdapterTools.Reference(parameters, 0, "Packages"),
                Results = VerifyAdapterTools.Reference(parameters, 1, "Results"),
                Repairs = parameters.Length > 2 ? VerifyAdapterTools.Reference(parameters, 2, "Repairs") : null,
                Repaired = parameters.Length > 3 ? VerifyAdapterTools.Reference(parameters, 3, "Repaired") : null
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
