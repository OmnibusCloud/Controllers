using Microsoft.Extensions.Logging;
using OutWit.Controller.CalculiX.Activities;
using OutWit.Controller.CalculiX.Extraction;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.CalculiX.Runtime;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.CalculiX.Adapters;

internal sealed class WitActivityAdapterCcxSolve : WitActivityAdapterFunction<WitActivityCcxSolve>
{
    #region Constants

    private const string JOB_NAME = "job";

    private const string SCRATCH_ROOT = "outwit-ccx";

    /// <summary>
    /// Element count the work estimate is normalized to. Direct sparse solves
    /// scale supra-linearly with mesh size; the exponent is an initial value
    /// to be recalibrated against the reference benchmark deck.
    /// </summary>
    private const int REFERENCE_ELEMENTS = 30_000;

    private const double WORK_EXPONENT = 1.5;

    #endregion

    #region Constructors

    public WitActivityAdapterCcxSolve(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCcxSolve activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Task, out CcxTaskData? task) || task == null)
            throw new InvalidOperationException("Failed to get parameter 'Task'.");

        var solverPath = CcxBinaryResolver.Resolve(GetType().Assembly.Location, Logger)
            ?? throw new InvalidOperationException(
                "ccx not found in the CalculiX controller module. Ensure the module includes the bundled solver for this platform.");

        var scratchDirectory = Path.Combine(Path.GetTempPath(), SCRATCH_ROOT, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDirectory);

        try
        {
            var deckPath = await BlobService.GetLocalPathAsync(task.DeckBlobId);
            File.Copy(deckPath, Path.Combine(scratchDirectory, $"{JOB_NAME}.inp"));

            var outcome = await CcxProcessRunner.RunAsync(solverPath, JOB_NAME, scratchDirectory, task.Threads);

            var frdPath = ExistingArtifact(scratchDirectory, ".frd");
            var datPath = ExistingArtifact(scratchDirectory, ".dat");

            var result = new CcxResultData
            {
                VariantIndex = task.VariantIndex,
                ExitCode = outcome.ExitCode,
                SolveSeconds = outcome.ElapsedSeconds,
                LogTail = outcome.ExitCode == 0 ? null : outcome.LogTail
            };

            if (frdPath != null)
                result.FrdBlobId = await BlobService.UploadFileAsync(frdPath);

            if (datPath != null)
                result.DatBlobId = await BlobService.UploadFileAsync(datPath);

            // A nonzero exit is data, not a task failure: the orchestration
            // records the failed variant and moves on; only infrastructure
            // errors (blob transfer, missing solver) throw out of here.
            if (outcome.ExitCode == 0)
                result.ResponseRow = CcxResponseExtractor.Extract(frdPath, datPath, task.Extraction);

            if (!pool.TrySetValue(activity.ReturnReference, result))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
        }
        finally
        {
            TryDeleteScratch(scratchDirectory);
        }
    }

    protected override double EstimateWork(WitActivityCcxSolve activity, IWitVariablesCollection pool)
    {
        if (!pool.TryGetValue(activity.Task, out CcxTaskData? task) || task == null || task.ElementCount <= 0)
            return 1.0;

        return System.Math.Pow(task.ElementCount / (double)REFERENCE_ELEMENTS, WORK_EXPONENT);
    }

    private static string? ExistingArtifact(string directory, string extension)
    {
        var path = Path.Combine(directory, JOB_NAME + extension);
        return File.Exists(path) ? path : null;
    }

    private void TryDeleteScratch(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Ccx.Solve: failed to delete scratch directory {Directory}.", directory);
        }
    }

    #endregion

    #region Parsing

    protected override WitActivityCcxSolve CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference task)
                throw new ArgumentException("Parameter 'Task' must be a variable reference.");

            return new WitActivityCcxSolve
            {
                Task = task
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
