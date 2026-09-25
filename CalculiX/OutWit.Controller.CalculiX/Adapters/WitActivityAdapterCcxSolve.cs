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

    private const string SCRATCH_LABEL = "calculix";

    /// <summary>
    /// Element count the work estimate is normalized to. Direct sparse solves
    /// scale supra-linearly with mesh size; the exponent is an initial value
    /// to be recalibrated against the reference benchmark deck.
    /// </summary>
    private const int REFERENCE_ELEMENTS = 30_000;

    private const double WORK_EXPONENT = 1.5;

    #endregion

    #region Constructors

    public WitActivityAdapterCcxSolve(IWitProcessingManager processingManager, IWitBlobService blobService, IWitTempStorage tempStorage, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        TempStorage = tempStorage;
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

        // The solve's scratch is a scope of the temp folder the host hands the
        // controller (the client's controllers' temp folder), never a folder
        // of the controller's own.
        var scratchDirectory = TempStorage.CreateScope(SCRATCH_LABEL);

        try
        {
            var deckPath = await BlobService.GetLocalPathAsync(task.DeckBlobId);
            var jobDeckPath = Path.Combine(scratchDirectory, $"{JOB_NAME}.inp");
            File.Copy(deckPath, jobDeckPath);

            // The job's cancellation must reach the RUNNING solver: without
            // the token a cancelled overnight sweep keeps every node's
            // in-flight ccx solving to completion, and cancel only takes
            // effect between activities.
            var cancellation = ProcessingManager.CancellationToken(status.JobId);

            // A solve that may go through SPOOLES gets one equation-solver thread:
            // multithreaded SPOOLES returns a wrong field now and then, with exit 0.
            var outcome = await CcxProcessRunner.RunAsync(
                solverPath, JOB_NAME, scratchDirectory, task.Threads, CcxEquationSolver.ThreadsFor(jobDeckPath), cancellation);

            // A killed solve is the user's verdict, not the deck's — it must
            // surface as cancellation, never be harvested as a red variant,
            // and no artifacts of a killed run get uploaded.
            cancellation.ThrowIfCancellationRequested();

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
            // Extraction itself follows the same rule — a parsing surprise
            // must not turn a finished solve into a failure, so it degrades
            // to an empty row with the artifacts still uploaded.
            if (outcome.ExitCode == 0)
            {
                try
                {
                    result.ResponseRow = CcxResponseExtractor.Extract(jobDeckPath, frdPath, datPath, task.Extraction);
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e, "Ccx.Solve: response extraction failed — the row stays empty, artifacts remain.");
                    result.ResponseRow = new CcxResponseRowData();
                }
            }

            if (!pool.TrySetValue(activity.ReturnReference, result))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
        }
        finally
        {
            DeleteScratch(scratchDirectory);
        }
    }

    public override async Task<IWitBenchmarkResult> RunBenchmark(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        var solverPath = CcxBinaryResolver.Resolve(GetType().Assembly.Location, Logger);
        if (solverPath == null)
        {
            // No bundled solver for this platform: the node reports the
            // default (unranked) score instead of failing registration.
            Logger.LogWarning("Ccx.Solve benchmark: bundled ccx not found — reporting the default score.");
            return OutWit.Engine.Data.Benchmark.WitBenchmarkResult.Default;
        }

        var result = await CcxBenchmark.MeasureAsync(solverPath, TempStorage, options, cancellationToken);

        Logger.LogInformation(
            "Ccx.Solve benchmark: {Rate:F3} {Unit} (median of {Runs} reference solves: {RunTimes} s; warm-up {Warmup} s)",
            result.Rate, result.Unit, result.Iterations, result.Custom?["runs_s"], result.Custom?["warmup_s"]);

        return result;
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
        if (!File.Exists(path))
            return null;

        // ccx touches artifacts it has nothing to write into — a deck without
        // *NODE PRINT requests leaves a zero-byte .dat next to a full .frd.
        // An empty artifact is no artifact, and the blob service rightly
        // refuses empty payloads (found on the first live sweep).
        return new FileInfo(path).Length > 0 ? path : null;
    }

    private void DeleteScratch(string directory)
    {
        // The scope goes back to the host's temp folder; a file still held
        // open keeps it, and the client takes it when it next starts.
        TempStorage.DeleteScope(directory);
        if (Directory.Exists(directory))
            Logger.LogWarning("Ccx.Solve: failed to delete scratch directory {Directory}.", directory);
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

    private IWitTempStorage TempStorage { get; }

    #endregion
}
