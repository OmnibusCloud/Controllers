using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Inspection;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// One variant's run from task to result: the scratch, the materialised
/// case, the refusals (recipe, files, run-time code), the steps, the facts
/// read from the logs, the responses, the notes (in the log tail, by
/// <see cref="FoamNoteLog"/>), the artifact. A refused or failed variant is
/// a result with the reasons; only infrastructure errors throw.
/// </summary>
public sealed class FoamCaseSession
{
    #region Constants

    private const string SCRATCH_LABEL = "openfoam";

    private const string CASE_DIRECTORY = "case";

    private const string ARTIFACT_NAME = "artifact.zip";

    #endregion

    #region Constructors

    /// <summary>
    /// A session for one task.
    /// </summary>
    /// <param name="kit">The resolved kit.</param>
    /// <param name="blobService">The node's blob service.</param>
    /// <param name="tempStorage">The host's temp folder, where the run's scratch lives.</param>
    /// <param name="logger">Diagnostics sink.</param>
    public FoamCaseSession(FoamKit kit, IWitBlobService blobService, IWitTempStorage tempStorage, ILogger? logger = null)
    {
        Kit = kit;
        BlobService = blobService;
        TempStorage = tempStorage;
        Logger = logger;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs the task.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="cancellationToken">Reaches the running step's process tree.</param>
    /// <returns>The result, refused, failed or complete.</returns>
    /// <exception cref="InvalidOperationException">The host's temp folder has whitespace in its path and no whitespace-free form.</exception>
    public async Task<FoamResultData> RunAsync(FoamTaskData task, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var scratch = FoamScratchPath.CreateScratch(TempStorage, SCRATCH_LABEL);
        var result = new FoamResultData { VariantIndex = task.VariantIndex };

        // Everything after the scratch exists runs inside the try: whatever
        // throws, the scratch goes back to the host's temp folder.
        try
        {
            var caseDirectory = Path.Combine(scratch.UsablePath, CASE_DIRECTORY);
            Directory.CreateDirectory(caseDirectory);

            // What the case's data and the variant's values decide is decided
            // before a byte is downloaded; the files' contents (tokens,
            // run-time code) after.
            var rejections = new List<string>(FoamCaseRules.Validate(task.Case, Kit.HasExecutable));
            rejections.AddRange(FoamTemplating.CheckSubstitutions(task.Substitutions));

            // What the run says of its responses besides their values: a
            // swept reference value a re-measured force follows, a value left
            // out and why. Written beside the logs as log.responses, and into
            // the result's log tail as note lines (FoamNoteLog).
            var notes = new List<FoamNote>();
            if (rejections.Count == 0)
            {
                rejections.AddRange(await FoamCaseMaterializer.MaterializeAsync(task, caseDirectory, BlobService, cancellationToken));
                var request = await FoamResponseFollowing.FollowAsync(task, caseDirectory, BlobService, notes, cancellationToken);
                rejections.AddRange(FoamFunctionObjectWriter.Write(caseDirectory, request, task.Substitutions));
            }

            if (rejections.Count == 0)
                rejections.AddRange(FoamCaseInspector.Inspect(caseDirectory, KitHasLibrary));

            // A refused variant runs nothing: its rejections say why, and the
            // notes of a run it never had stay out of its tail, which stays
            // empty as before - a client shows the rejections of such a row.
            if (rejections.Count > 0)
            {
                result.Rejections = rejections;
                result.TotalSeconds = stopwatch.Elapsed.TotalSeconds;
                return result;
            }

            // The case rules refused a task without a case or a recipe above; these are the invariants, not branches.
            var data = task.Case ?? throw new InvalidOperationException("The task carries no case.");
            var recipe = data.Recipe ?? throw new InvalidOperationException("The task carries no recipe.");

            // What the task would do without doing what was meant (ranks that
            // do nothing, a setFields over a swept field): said, not refused.
            notes.AddRange(await FoamCaseAdvisor.AdviseAsync(task, BlobService, cancellationToken));
            var ranks = FoamDecomposition.Ranks(data.Threads);
            var environment = Kit.EnvironmentFor(scratch.UsablePath);
            var runner = new FoamCaseRunner(Kit, caseDirectory, environment, ranks, Logger);

            // The statistics, min/max and probes the recipe measures after
            // the solve are measured during it as well: a post step reads
            // the latest written time, not the final one.
            var measuredInSolve = FoamSolveResponses.MeasuredInSolve(caseDirectory, recipe, data.Extraction);
            var report = await runner.RunAsync(recipe, measuredInSolve, cancellationToken);

            // A killed run is the user's verdict, not the case's - it must
            // surface as cancellation, never be harvested as a red variant.
            cancellationToken.ThrowIfCancellationRequested();

            result.Steps = report.Steps;
            result.ExitCode = report.ExitCode;
            result.FailedStep = report.FailedStep;

            ReadFacts(result, caseDirectory, recipe, report);

            var extracted = new List<FoamNote>();
            if (report.Succeeded)
            {
                result.ResponseRow = ExtractResponses(caseDirectory, data.Extraction, result.FinalTime, notes, extracted);
                await UploadArtifactAsync(result, caseDirectory, scratch.UsablePath, data.ArtifactPolicy, cancellationToken);
            }
            else if (data.ArtifactPolicy?.Logs == true)
            {
                // A failed run still owes its logs when they were asked for: a
                // diverged six-hour transient is not explained by sixty lines of tail.
                await UploadArtifactAsync(result, caseDirectory, scratch.UsablePath, new FoamArtifactPolicyData { Logs = true }, cancellationToken);
            }

            // The notes ride inside the log tail, after the failed step's own
            // tail (a failed run's only), in the order they arose - before the
            // run, of its steps, of its responses: the result keeps the
            // shipped wire layout, which every installed client reads. A
            // variant without notes keeps the tail it always had.
            result.LogTail = FoamNoteLog.Compose(report.LogTail, [.. notes, .. report.Notes, .. extracted]);
            result.TotalSeconds = stopwatch.Elapsed.TotalSeconds;
            return result;
        }
        finally
        {
            DeleteScratch(scratch);
        }
    }

    private void ReadFacts(FoamResultData result, string caseDirectory, FoamRecipeData recipe, FoamRunReport report)
    {
        // The solver's own log: the first step that runs the application as a
        // solve (a later "<solver> -postProcess" step is a post step and has a
        // log of its own).
        var solverLogPath = report.LogPathOf(step => step.Utility == recipe.Application && !step.Arguments.Contains("-postProcess"))
                            ?? Path.Combine(caseDirectory, $"log.{recipe.Application}");
        var solverLog = FoamLogReader.Read(solverLogPath);
        result.Iterations = solverLog.Iterations;
        result.FinalTime = solverLog.FinalTime;
        result.Converged = solverLog.Converged && !solverLog.Fatal && !solverLog.FloatingPointException;
        result.FinalResiduals = solverLog.FinalResiduals
            .Select(residual => new FoamResponseValueData { Name = $"residual.{residual.Key}", Value = residual.Value })
            .ToList();

        var checkMeshLog = report.LogPathOf(step => step.Utility == "checkMesh") ?? Path.Combine(caseDirectory, "log.checkMesh");
        var (verdict, _) = FoamCheckMeshReader.Read(checkMeshLog);
        result.CheckMeshVerdict = verdict;

        // Only the logs this run wrote: the base tree may not carry logs at all
        // (the materializer refuses them), so these are the steps' own. They
        // run in step order, so the last count is the latest mesh a step
        // reported - snappyHexMesh's, not the background mesh blockMesh made.
        var warnings = 0;
        long logged = 0;
        foreach (var log in report.LogPaths)
        {
            var facts = FoamLogReader.Read(log);
            warnings += facts.WarningCount;
            if (facts.CellCount > 0)
                logged = facts.CellCount;
        }

        result.WarningCount = warnings;

        // The mesh on disk outranks the logs: it is the mesh the run ended with
        // whatever step wrote it, one that prints no count included.
        var decomposed = report.Steps.Any(step => step.Utility == recipe.Application && step.Ranks > 1);
        var meshed = FoamMeshReader.CellCount(caseDirectory, decomposed);
        result.CellCount = meshed > 0 ? meshed : logged;
    }

    private FoamResponseRowData ExtractResponses(string caseDirectory, FoamExtractionRequestData? extraction, double finalTime, IReadOnlyList<FoamNote> before, List<FoamNote> extracted)
    {
        // A parsing surprise must not turn a finished run into a failure: the
        // row degrades to empty and the run's other facts stand.
        try
        {
            // A response whose last row is not the run's final time is left
            // out and named in log.responses, which travels with the logs;
            // an unknown final time (no solver log) checks nothing.
            var row = FoamResponseExtractor.Extract(caseDirectory, extraction, finalTime > 0 ? finalTime : null, extracted);
            List<FoamNote> notes = [.. before, .. extracted];
            foreach (var sentence in FoamResponseExtractor.Sentences(notes))
                Logger?.LogInformation("Foam.Run: {Note}", sentence);
            FoamResponseExtractor.WriteNotes(caseDirectory, notes);
            return row;
        }
        catch (Exception e)
        {
            // The row is empty: what the extraction said of its columns says nothing more.
            extracted.Clear();
            Logger?.LogWarning(e, "Foam.Run: response extraction failed - the row stays empty.");
            return new FoamResponseRowData();
        }
    }

    private async Task UploadArtifactAsync(FoamResultData result, string caseDirectory, string scratch, FoamArtifactPolicyData? policy, CancellationToken cancellationToken)
    {
        if (policy == null || policy.IsEmpty())
            return;

        var zipPath = Path.Combine(scratch, ARTIFACT_NAME);
        var bytes = FoamArtifactPacker.Pack(caseDirectory, policy, zipPath);
        cancellationToken.ThrowIfCancellationRequested();

        result.ArtifactBlobId = await BlobService.UploadFileAsync(zipPath);
        result.ArtifactBytes = bytes;
    }

    /// <summary>
    /// Whether a <c>libs</c> entry names a library of the kit. OpenFOAM
    /// accepts the entry as <c>"libforces.so"</c> or as the bare
    /// <c>forces</c>; the file in the kit is <c>libforces.so</c> (Linux),
    /// <c>libforces.dylib</c> (macOS) or <c>libforces.dll</c> (Windows).
    /// </summary>
    private bool KitHasLibrary(string library)
    {
        var libbin = Kit.Environment.Get("FOAM_LIBBIN", Kit.Root);
        if (libbin == null || !Directory.Exists(libbin))
            return false;

        var name = library.Trim('"');
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.EndsWith(".so", StringComparison.Ordinal))
            stem = stem[..^3];
        if (stem.StartsWith("lib", StringComparison.Ordinal))
            stem = stem[3..];
        if (stem.Length == 0)
            return false;

        return Directory.EnumerateFiles(libbin, $"lib{stem}.*", SearchOption.AllDirectories).Any()
               || Directory.EnumerateFiles(libbin, $"{stem}.*", SearchOption.AllDirectories).Any();
    }

    private void DeleteScratch(FoamScratch scratch)
    {
        if (!FoamScratchPath.Delete(TempStorage, scratch))
            Logger?.LogWarning("Foam.Run: failed to delete scratch directory {Directory}.", scratch.ScopePath);
    }

    #endregion

    #region Properties

    private FoamKit Kit { get; }

    private IWitBlobService BlobService { get; }

    private IWitTempStorage TempStorage { get; }

    private ILogger? Logger { get; }

    #endregion
}
