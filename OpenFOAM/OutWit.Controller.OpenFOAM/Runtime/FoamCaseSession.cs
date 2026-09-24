using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Recipes;
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

    private const string SCRATCH_ROOT = "outwit-foam";

    private const string CASE_DIRECTORY = "case";

    private const string ARTIFACT_NAME = "artifact.zip";

    #endregion

    #region Constructors

    /// <summary>
    /// A session for one task.
    /// </summary>
    /// <param name="kit">The resolved kit.</param>
    /// <param name="blobService">The node's blob service.</param>
    /// <param name="logger">Diagnostics sink.</param>
    public FoamCaseSession(FoamKit kit, IWitBlobService blobService, ILogger? logger = null)
    {
        Kit = kit;
        BlobService = blobService;
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
    /// <exception cref="InvalidOperationException">The scratch path is unusable (a space in it, plan D-16).</exception>
    public async Task<FoamResultData> RunAsync(FoamTaskData task, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var scratch = CreateScratch();
        var caseDirectory = Path.Combine(scratch, CASE_DIRECTORY);
        Directory.CreateDirectory(caseDirectory);

        var result = new FoamResultData { VariantIndex = task.VariantIndex };

        try
        {
            var rejections = new List<string>();
            rejections.AddRange(FoamRecipeValidator.Validate(task.Recipe, Kit));
            rejections.AddRange(await FoamCaseMaterializer.MaterializeAsync(task, caseDirectory, BlobService, cancellationToken));
            rejections.AddRange(FoamFunctionObjectWriter.Write(caseDirectory, task.Extraction));
            if (rejections.Count == 0)
                rejections.AddRange(FoamCaseInspector.Inspect(caseDirectory, KitHasLibrary));

            if (rejections.Count > 0)
            {
                result.Rejections = rejections;
                result.TotalSeconds = stopwatch.Elapsed.TotalSeconds;
                return result;
            }

            var recipe = task.Recipe!;
            var ranks = FoamDecomposition.Ranks(task.Threads);
            var environment = Kit.EnvironmentFor(scratch);
            var runner = new FoamCaseRunner(Kit, caseDirectory, environment, ranks, Logger);

            var report = await runner.RunAsync(recipe, cancellationToken);

            // A killed run is the user's verdict, not the case's - it must
            // surface as cancellation, never be harvested as a red variant.
            cancellationToken.ThrowIfCancellationRequested();

            result.Steps = report.Steps;
            result.ExitCode = report.ExitCode;
            result.FailedStep = report.FailedStep;
            result.LogTail = report.LogTail;

            ReadFacts(result, caseDirectory, recipe);

            if (report.Succeeded)
            {
                result.ResponseRow = ExtractResponses(caseDirectory, task.Extraction);
                await UploadArtifactAsync(result, caseDirectory, scratch, task.ArtifactPolicy, cancellationToken);
            }

            result.TotalSeconds = stopwatch.Elapsed.TotalSeconds;
            return result;
        }
        finally
        {
            TryDeleteScratch(scratch);
        }
    }

    private void ReadFacts(FoamResultData result, string caseDirectory, FoamRecipeData recipe)
    {
        var solverLog = FoamLogReader.Read(Path.Combine(caseDirectory, $"log.{recipe.Application}"));
        result.Iterations = solverLog.Iterations;
        result.FinalTime = solverLog.FinalTime;
        result.Converged = solverLog.Converged && !solverLog.Fatal && !solverLog.FloatingPointException;
        result.FinalResiduals = solverLog.FinalResiduals
            .Select(residual => new FoamResponseValueData { Name = $"residual.{residual.Key}", Value = residual.Value })
            .ToList();

        var (verdict, checkedCells) = FoamCheckMeshReader.Read(Path.Combine(caseDirectory, "log.checkMesh"));
        result.CheckMeshVerdict = verdict;

        var warnings = 0;
        long cells = checkedCells;
        foreach (var log in Directory.EnumerateFiles(caseDirectory, "log.*"))
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

    /// <summary>
    /// A private scratch for the run, under the node's temp. No space in the
    /// path (plan D-16: OpenFOAM strips whitespace from paths): on Windows a
    /// temp under a profile with a space is used through its 8.3 short form;
    /// a node whose temp has a space and no short form cannot run cases, and
    /// says so.
    /// </summary>
    /// <returns>The scratch directory, created.</returns>
    /// <exception cref="InvalidOperationException">The temp path contains a space and has no space-free form.</exception>
    public static string CreateScratch()
    {
        var root = Path.Combine(Path.GetTempPath(), SCRATCH_ROOT);
        Directory.CreateDirectory(root);

        var usable = FoamScratchPath.WithoutSpaces(root)
            ?? throw new InvalidOperationException($"The node's temp path contains a space ('{Path.GetTempPath()}') and has no short form; OpenFOAM cannot run under it. Point TMPDIR/TEMP at a space-free directory.");

        var scratch = Path.Combine(usable, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(scratch, "home"));
        Directory.CreateDirectory(Path.Combine(scratch, "tmp"));
        return scratch;
    }

    private void TryDeleteScratch(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e)
        {
            Logger?.LogWarning(e, "Foam.Run: failed to delete scratch directory {Directory}.", directory);
        }
    }

    #endregion

    #region Properties

    private FoamKit Kit { get; }

    private IWitBlobService BlobService { get; }

    private ILogger? Logger { get; }

    #endregion
}
