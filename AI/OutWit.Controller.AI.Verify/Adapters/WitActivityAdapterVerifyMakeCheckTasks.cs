using System.Collections;
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

internal sealed class WitActivityAdapterVerifyMakeCheckTasks : WitActivityAdapterFunction<WitActivityVerifyMakeCheckTasks>
{
    #region Constructors

    public WitActivityAdapterVerifyMakeCheckTasks(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyMakeCheckTasks activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetObject(activity.Candidates, out var candidatesObject) || candidatesObject is not IEnumerable candidatesEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Candidates'.");

        if (!pool.TryGetValue(activity.Checker, out Guid checkerBlobId) || checkerBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'Checker'.");

        pool.TryGetValue(activity.Options, out VerifyOptionsData? options);

        var candidates = (await VerifyResultPackages.LoadAsync(candidatesEnumerable, BlobService))
            .SelectMany(batch => batch.Results)
            .ToList();

        var checkerPath = await BlobService.GetLocalPathAsync(checkerBlobId);
        var checker = VerifyCheckerSpec.Parse(await File.ReadAllTextAsync(checkerPath));

        var plan = VerifyCheckTaskBuilder.Build(candidates, checker);
        foreach (var note in plan.Skipped)
            Logger.LogInformation("Verify.MakeCheckTasks: skipped {Note}", note);

        if (plan.Tasks.Count == 0)
            throw new InvalidOperationException($"No candidate produced a checkable output ({candidates.Count} candidate(s), {plan.Skipped.Count} skipped).");

        Logger.LogInformation("Verify.MakeCheckTasks: {Checks} check task(s) from {Candidates} candidate(s), {Skipped} skipped",
            plan.Tasks.Count, candidates.Count, plan.Skipped.Count);

        var batches = VerifyTasksetPlanner.Plan(plan.Tasks, options);
        foreach (var note in batches.Notes)
            Logger.LogInformation("Verify.MakeCheckTasks: {Note}", note);

        var packages = await VerifyTaskPackages.PublishAsync(batches.Batches, BlobService, Logger, "Verify.MakeCheckTasks");

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyMakeCheckTasks CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 3)
                throw new ArgumentException($"Expected 3 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference candidates)
                throw new ArgumentException("Parameter 'Candidates' must be a variable reference.");

            if (parameters[1] is not IWitReference checker)
                throw new ArgumentException("Parameter 'Checker' must be a variable reference.");

            if (parameters[2] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityVerifyMakeCheckTasks
            {
                Candidates = candidates,
                Checker = checker,
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
