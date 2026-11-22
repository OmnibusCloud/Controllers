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
    #region Constants

    private const string SKIPPED = "candidate(s) without a check";
    private const int SHOWN_PROBLEMS = 5;

    #endregion

    #region Constructors

    public WitActivityAdapterVerifyMakeCheckTasks(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, VerifyLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyMakeCheckTasks activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var ledgerBlobId = VerifyAdapterTools.Blob(pool, activity.Candidates, "Candidates");

        if (!pool.TryGetValue(activity.Checker, out Guid checkerBlobId) || checkerBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'Checker'.");

        pool.TryGetValue(activity.Options, out VerifyOptionsData? options);

        var ledgerPath = await BlobService.GetLocalPathAsync(ledgerBlobId);
        var checker = await VerifyCheckerSpec.ReadAsync(await BlobService.GetLocalPathAsync(checkerBlobId));

        // As for a taskset: a runtime this controller does not know has no node that could run it.
        var unknown = VerifyTasksetChecks.UnknownRuntimes([new VerifyTaskData { RuntimeId = checker.RuntimeId }]);
        if (unknown.Count > 0)
            throw new InvalidOperationException($"The checker names a runtime this controller does not have: {unknown[0]}");

        // One settled result a generator: what is checked is what the corpus holds. The
        // candidates are read from the ledger one at a time, twice: once to count the checks (the
        // count decides how the packages are cut), once to make them. A candidate is as large as
        // a task's output may be, and a run has as many as it has tasks.
        var candidates = 0;
        var checks = 0;
        var skipped = new VerifyMessageList();
        var notes = new VerifyMessageList();
        var unplanned = new VerifyMessageList();
        var clamped = new List<string>();

        foreach (var candidate in VerifyCheckTaskBuilder.ReadCandidates(ledgerPath))
        {
            candidates++;

            var task = VerifyCheckTaskBuilder.CheckTask(candidate, checker, out var why);
            if (task == null)
            {
                skipped.Add($"candidate {candidate.Index}: {why}");
                continue;
            }

            clamped.Clear();
            var error = VerifyTasksetPlanner.Admit(task, options?.DefaultLimits, clamped, out _, out _);
            notes.AddRange(clamped);
            if (error != null)
                unplanned.Add(error);
            else
                checks++;
        }

        foreach (var note in skipped.ToList(SKIPPED))
            Logger.LogInformation("Verify.MakeCheckTasks: skipped {Note}", note);

        if (checks == 0 && unplanned.Count == 0)
            throw new InvalidOperationException($"No candidate produced a checkable output ({candidates} candidate(s), {skipped.Count} skipped).");

        Logger.LogInformation("Verify.MakeCheckTasks: {Checks} check task(s) from {Candidates} candidate(s), {Skipped} skipped", checks, candidates, skipped.Count);

        foreach (var note in notes.ToList(VerifyTasksetScan.NOTES))
            Logger.LogInformation("Verify.MakeCheckTasks: {Note}", note);

        // Refused as a taskset is: one check that cannot be planned fails the activity, by its
        // index, before anything is stored.
        if (unplanned.Count > 0)
            throw new InvalidOperationException($"{unplanned.Count} check task(s) cannot be planned: {string.Join("; ", unplanned.Shown.Take(SHOWN_PROBLEMS))}");

        var packages = await PublishAsync(ledgerPath, checker, checks, options);

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    /// <summary>
    /// Makes the check tasks one at a time and stores them a package at a time: the checks of a
    /// run are never together in memory.
    /// </summary>
    private async Task<List<VerifyTaskPackageData>> PublishAsync(string ledgerPath, VerifyCheckerSpec checker, int checks, VerifyOptionsData? options)
    {
        var publisher = new VerifyPackagePublisher(checks, BlobService);
        var cutter = new VerifyBatchCutter(checker.RuntimeId, options, VerifyTasksetPlanner.BatchSize(options, out var sizeNote));
        if (sizeNote != null)
            Logger.LogInformation("Verify.MakeCheckTasks: {Note}", sizeNote);

        var clamped = new List<string>();
        foreach (var candidate in VerifyCheckTaskBuilder.ReadCandidates(ledgerPath))
        {
            var task = VerifyCheckTaskBuilder.CheckTask(candidate, checker, out _);
            if (task == null)
                continue;

            // Every check was admitted when the candidates were counted; here its limits are set.
            clamped.Clear();
            if (VerifyTasksetPlanner.Admit(task, options?.DefaultLimits, clamped, out var bytes, out var output) != null)
                continue;

            if (cutter.Add(task, bytes, output) is { } closed)
                await publisher.AddAsync(closed);
        }

        if (cutter.Finish() is { } last)
            await publisher.AddAsync(last);

        var packages = await publisher.CompleteAsync();
        Logger.LogInformation("Verify.MakeCheckTasks: {Tasks} task(s) in {Batches} batch(es) stored as {Packages} package blob(s), {Bytes} bytes",
            publisher.Tasks, publisher.Batches, packages.Count, publisher.Bytes);

        return packages;
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
