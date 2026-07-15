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
        var jsonl = await File.ReadAllTextAsync(tasksetPath);

        var parsed = VerifyTasksetParser.Parse(jsonl);
        var report = new VerifyPreflightData
        {
            WellFormed = parsed.Errors.Count == 0 && parsed.Tasks.Count > 0,
            TaskCount = parsed.Tasks.Count,
            EstimatedInputBytes = parsed.Tasks.Sum(task => (long)task.Sources.Sum(s => s.Content.Length)),
            Messages = [.. parsed.Errors]
        };

        if (parsed.Tasks.Count == 0 && parsed.Errors.Count == 0)
            report.Messages.Add("Taskset is empty.");

        var runtimeIds = parsed.Tasks.Select(task => task.RuntimeId).Distinct(StringComparer.Ordinal).ToList();
        report.RuntimeIds = runtimeIds;
        report.UnknownRuntimeIds = runtimeIds.Where(id => VerifyRuntimeCatalog.Find(id) == null).ToList();
        foreach (var unknown in report.UnknownRuntimeIds)
            report.Messages.Add($"runtime '{unknown}' is not known to this build.");

        if (parsed.Tasks.Count > 0)
        {
            var plan = VerifyTasksetPlanner.Plan(parsed.Tasks, options);
            report.BatchCount = plan.Batches.Count;
            report.Messages.AddRange(plan.Notes);
        }

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
