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
/// read from the logs, the responses, the artifact. A refused or failed
/// variant is a result with the reasons; only infrastructure errors throw.
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

            // What the case's data decides is decided before a byte is
            // downloaded; the files' contents (tokens, run-time code) after.
            var rejections = new List<string>(FoamCaseRules.Validate(task.Case, Kit.HasExecutable));
            if (rejections.Count == 0)
            {
                rejections.AddRange(await FoamCaseMaterializer.MaterializeAsync(task, caseDirectory, BlobService, cancellationToken));
                rejections.AddRange(FoamFunctionObjectWriter.Write(caseDirectory, task.Case?.Extraction));
            }

            if (rejections.Count == 0)
                rejections.AddRange(FoamCaseInspector.Inspect(caseDirectory, KitHasLibrary));

            if (rejections.Count > 0)
            {
                result.Rejections = rejections;
                result.TotalSeconds = stopwatch.Elapsed.TotalSeconds;
                return result;
            }

            // The case rules refused a task without a case or a recipe above; these are the invariants, not branches.
            var data = task.Case ?? throw new InvalidOperationException("The task carries no case.");
            var recipe = data.Recipe ?? throw new InvalidOperationException("The task carries no recipe.");
            var ranks = FoamDecomposition.Ranks(data.Threads);
            var environment = Kit.EnvironmentFor(scratch.UsablePath);
            var runner = new FoamCaseRunner(Kit, caseDirectory, environment, ranks, Logger);

            var report = await runner.RunAsync(recipe, cancellationToken);

            // A killed run is the user's verdict, not the case's - it must
            // surface as cancellation, never be harvested as a red variant.
            cancellationToken.ThrowIfCancellationRequested();

            result.Steps = report.Steps;
            result.ExitCode = report.ExitCode;
            result.FailedStep = report.FailedStep;
            result.LogTail = report.LogTail;

            ReadFacts(result, caseDirectory, recipe, report);

            if (report.Succeeded)
            {
                result.ResponseRow = ExtractResponses(caseDirectory, data.Extraction);
                await UploadArtifactAsync(result, caseDirectory, scratch.UsablePath, data.ArtifactPolicy, cancellationToken);
            }
            else if (data.ArtifactPolicy?.Logs == true)
            {
                // A failed run still owes its logs when they were asked for: a
                // diverged six-hour transient is not explained by sixty lines of tail.
                await UploadArtifactAsync(result, caseDirectory, scratch.UsablePath, new FoamArtifactPolicyData { Logs = true }, cancellationToken);
            }

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
        var (verdict, checkedCells) = FoamCheckMeshReader.Read(checkMeshLog);
        result.CheckMeshVerdict = verdict;

        // Only the logs this run wrote: the base tree may not carry logs at all
        // (the materializer refuses them), so these are the steps' own.
        var warnings = 0;
        long cells = checkedCells;
        foreach (var log in report.LogPaths)
        {
            var facts = FoamLogReader.Read(log);
            warnings += facts.WarningCount;
            if (cells == 0 && facts.CellCount > 0)
                cells = facts.CellCount;
        }

        result.WarningCount = warnings;
        result.CellCount = cells;
    }

    private FoamResponseRowData ExtractResponses(string caseDirectory, FoamExtractionRequestData? extraction)
    {
        // A parsing surprise must not turn a finished run into a failure: the
        // row degrades to empty and the run's other facts stand.
        try
        {
            return FoamResponseExtractor.Extract(caseDirectory, extraction);
        }
        catch (Exception e)
        {
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
