using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifyPreflight : WitActivityAdapterFunction<WitActivityVerifyPreflight>
{
    #region Constructors

    public WitActivityAdapterVerifyPreflight(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyPreflight activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Taskset, out Guid tasksetBlobId) || tasksetBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'Taskset'.");

        pool.TryGetValue(activity.Options, out VerifyOptionsData? options);

        var tasksetPath = await BlobService.GetLocalPathAsync(tasksetBlobId);

        // One pass over the taskset, a line at a time, nothing kept: what it says is counts and
        // a bounded number of bounded messages, whatever the taskset's size.
        VerifyTasksetScan scan;
        await using (var taskset = VerifyAdapterTools.OpenTaskset(tasksetPath))
            scan = VerifyTasksetScanner.Scan(taskset, options, null);

        var report = VerifyPreflightReport.Of(scan);

        if (!pool.TrySetValue(activity.ReturnReference, report))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyPreflight CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference taskset)
                throw new ArgumentException("Parameter 'Taskset' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityVerifyPreflight
            {
                Taskset = taskset,
                Options = options
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
