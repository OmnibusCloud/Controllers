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

internal sealed class WitActivityAdapterVerifyRepairs : WitActivityAdapterFunction<WitActivityVerifyRepairs>
{
    #region Constructors

    public WitActivityAdapterVerifyRepairs(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, VerifyLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyRepairs activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var issued = VerifyAdapterTools.TaskPackages(pool, activity.Packages, "Packages");
        var returned = VerifyAdapterTools.ResultPackages(pool, activity.Results, "Results");

        // What came back is taken in under its tickets; what has no verdict yet goes out once more.
        var main = await VerifySettlement.ReadMainAsync(issued, returned, BlobService);
        var packages = await VerifySettlement.PublishRepairsAsync(main, BlobService);

        Logger.LogInformation("Verify.Repairs: {Tasks} task(s) without a verdict go out once more in {Packages} package(s); {Rejected} of {Returned} result package(s) refused",
            packages.Sum(package => (long)package.TaskCount), packages.Count, main.RejectedTotal, returned.Count);

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyRepairs CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            return new WitActivityVerifyRepairs
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
