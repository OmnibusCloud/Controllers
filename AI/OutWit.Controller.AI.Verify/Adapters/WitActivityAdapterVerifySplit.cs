using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifySplit : WitActivityAdapterFunction<WitActivityVerifySplit>
{
    #region Constants

    private const int SHOWN_PROBLEMS = 5;
    private const string SPOOL_FOLDER = "tasks";

    #endregion

    #region Constructors

    public WitActivityAdapterVerifySplit(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, VerifyLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifySplit activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Taskset, out Guid tasksetBlobId) || tasksetBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'Taskset'.");

        pool.TryGetValue(activity.Options, out VerifyOptionsData? options);

        var tasksetPath = await BlobService.GetLocalPathAsync(tasksetBlobId);

        // The taskset is read a line at a time and its tasks wait in working files of the
        // server's own until their packages are stored: memory holds a task, a batch and a
        // package, whatever the taskset's size.
        using var work = new VerifyWorkFolder(Services);
        using var spool = new VerifyTaskSpool(Path.Combine(work.Path, SPOOL_FOLDER));

        VerifyTasksetScan scan;
        await using (var taskset = VerifyAdapterTools.OpenTaskset(tasksetPath))
            scan = VerifyTasksetScanner.Scan(taskset, options, spool);

        if (scan.Errors.Count > 0)
            throw new InvalidOperationException($"Taskset has {scan.Errors.Count} malformed line(s): {string.Join("; ", scan.Errors.Shown.Take(SHOWN_PROBLEMS))}");
        if (scan.Tasks == 0)
            throw new InvalidOperationException("Taskset is empty.");

        // A runtime this controller does not know has no node that could run it: the taskset is
        // refused here, by the runtime's name, and no node is asked.
        if (scan.UnknownTasks > 0)
            throw new InvalidOperationException($"Taskset names runtime(s) this controller does not have: {string.Join(", ", scan.UnknownRuntimeIds.Take(SHOWN_PROBLEMS))}");

        foreach (var note in scan.Notes.ToList(VerifyTasksetScan.NOTES))
            Logger.LogInformation("Verify.Split: {Note}", note);

        if (scan.PlanErrors.Count > 0)
            throw new InvalidOperationException($"Taskset has {scan.PlanErrors.Count} task(s) that cannot be planned: {string.Join("; ", scan.PlanErrors.Shown.Take(SHOWN_PROBLEMS))}");

        // The tasks leave the host as blobs; the collection Grid.ForEach fans out is references.
        var packages = await VerifyTaskPackages.PublishAsync(spool, options, BlobService, Logger, "Verify.Split");

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifySplit CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference taskset)
                throw new ArgumentException("Parameter 'Taskset' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityVerifySplit
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

    private IServiceProvider Services { get; }

    #endregion
}
