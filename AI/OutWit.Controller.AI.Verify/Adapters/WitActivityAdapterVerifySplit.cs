using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifySplit : WitActivityAdapterFunction<WitActivityVerifySplit>
{
    #region Constructors

    public WitActivityAdapterVerifySplit(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifySplit activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Taskset, out Guid tasksetBlobId) || tasksetBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'Taskset'.");

        pool.TryGetValue(activity.Options, out VerifyOptionsData? options);

        var tasksetPath = await BlobService.GetLocalPathAsync(tasksetBlobId);
        var jsonl = await File.ReadAllTextAsync(tasksetPath);

        var parsed = VerifyTasksetParser.Parse(jsonl);
        if (parsed.Errors.Count > 0)
            throw new InvalidOperationException($"Taskset has {parsed.Errors.Count} malformed line(s): {string.Join("; ", parsed.Errors.Take(5))}");
        if (parsed.Tasks.Count == 0)
            throw new InvalidOperationException("Taskset is empty.");

        var plan = VerifyTasksetPlanner.Plan(parsed.Tasks, options);
        foreach (var note in plan.Notes)
            Logger.LogInformation("Verify.Split: {Note}", note);

        if (!pool.TrySetCollection(activity.ReturnReference, plan.Batches))
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

    #endregion
}
