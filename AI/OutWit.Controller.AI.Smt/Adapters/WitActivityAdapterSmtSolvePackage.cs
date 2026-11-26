using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// <c>Smt.SolvePackage</c> on a node: the package's reference is checked, its blob is fetched,
/// checked and read, its problems are solved on the node's budget of sessions beside the problems
/// of the other packages of the process (<see cref="SmtSessionBudget.Node"/>,
/// <see cref="SmtPackageRunner"/>), and the rows go back as a blob. A package the node cannot work
/// on at all comes back with the code of why and no blob (<see cref="SmtResultPackageData.FaultCode"/>);
/// a cancelled package returns nothing; a failure of the node's own that has no code is reported in
/// fixed words.
/// </summary>
internal sealed class WitActivityAdapterSmtSolvePackage : WitActivityAdapterFunction<WitActivitySmtSolvePackage>
{
    #region Constants

    /// <summary>
    /// What a failure of the node's own says to whoever reads the job's status: fixed words. What
    /// happened is in the node's log; an exception's own text can name a folder of the node.
    /// </summary>
    public const string NODE_FAILURE = "Smt: this node could not work on the package for a reason of its own (its log says which)";

    private const string WORK_LABEL = "smt-package";

    private const string SANDBOX_MISSING = "this process has no sandbox for the solver";

    #endregion

    #region Constructors

    /// <summary>Makes the adapter of a host or a node.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="blobService">The host's blob service.</param>
    /// <param name="services">The host's services.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtSolvePackage(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;

        // Before the sandbox is first made (a benchmark may make it): the host's own files of a
        // session belong in the host's temp storage, the client's folder on a contributor's machine.
        SmtSandboxHost.UseTempStorage(services.GetService<IWitTempStorage>());
    }

    #endregion

    #region Processing

    /// <summary>Solves the package the activity names and returns the reference to its rows.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A task that ends when the rows are uploaded or the package is refused.</returns>
    protected override async Task Process(WitActivitySmtSolvePackage activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Package, out SmtTaskPackageData? package) || package == null)
            throw new InvalidOperationException("Failed to get parameter 'Package'.");

        // The job's token: a cancelled job hands out no more problems and its running sessions end
        // at their next read; the package returns nothing and goes elsewhere.
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);

        SmtResultPackageData result;
        try
        {
            result = await SolveAsync(package, status.JobId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Smt: a package failed on this node");
            throw new InvalidOperationException(NODE_FAILURE);
        }

        if (!pool.TrySetValue(activity.ReturnReference, result))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    /// <summary>
    /// Measures this node's rate on the reference set (<see cref="SmtBenchmark"/>); a node without
    /// a sandbox, without memory for one session, or that solves the set wrong reports the default
    /// (unranked) score: it would refuse the packages it was given, so it should not be given a share.
    /// </summary>
    /// <param name="options">The engine's benchmark options.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The rate in problems a second at the node's degree.</returns>
    public override async Task<IWitBenchmarkResult> RunBenchmark(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Task.Run(() => SmtBenchmark.Measure(options, cancellationToken), cancellationToken);

            Logger.LogInformation(
                "Smt.SolvePackage benchmark: {Rate:F2} {Unit} at degree {Degree} (median of {Runs} runs: {RunTimes} s; warm-up {Warmup} s)",
                result.Rate, result.Unit, result.Custom?[SmtBenchmark.DEGREE_KEY], result.Iterations, result.Custom?["runs_s"], result.Custom?["warmup_s"]);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A node that cannot solve the reference set must not score; it stays registered,
            // ranked last, with the reason in its log.
            Logger.LogWarning(exception, "Smt.SolvePackage benchmark failed - reporting the default score.");
            return WitBenchmarkResult.Default;
        }
    }

    /// <summary>The work of a package without opening its blob: its problems, the unit the benchmark's rate is in.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <returns>The estimate.</returns>
    protected override double EstimateWork(WitActivitySmtSolvePackage activity, IWitVariablesCollection pool)
    {
        return pool.TryGetValue(activity.Package, out SmtTaskPackageData? package) && package != null
            ? Math.Max(1, package.ProblemCount)
            : 1.0;
    }

    /// <summary>
    /// Solves one package: the reference is checked, the blob fetched and checked, the problems
    /// solved, the rows uploaded. A package this node does not work on comes back with its code.
    /// </summary>
    /// <param name="package">The reference.</param>
    /// <param name="jobId">The job, for the share's progress.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The reference to the rows, or a package with a fault code and no blob.</returns>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    internal async Task<SmtResultPackageData> SolveAsync(SmtTaskPackageData package, Guid jobId, CancellationToken cancellationToken)
    {
        // The work folder first: a storage with no folder to give fails the package here, before
        // the package is entered in its job's progress. Once it is entered, only the finally below
        // takes it out again, so nothing that can throw stands between the two.
        using var work = new SmtWorkFolder(Services, WORK_LABEL);
        var received = false;
        var solved = 0;

        var progress = SmtDeliveryProgress.Enter(jobId, package, SmtActivityProgressReporter.Create(ProcessingManager, jobId), Logger);
        try
        {
            SmtPackageRunner.EnsureAcceptable(package);

            var path = await SmtPackageTransfer.FetchAsync(BlobService, package, Logger, cancellationToken);
            progress.Received(package);
            received = true;

            // The package waits here with its blob downloaded until the packages open on this node
            // have handed out all of their problems; its turn is its place until its solve stage
            // is over, and the upload goes on without it.
            SmtWrittenResults written;
            var turn = await SmtSessionBudget.Node.EnterAsync(cancellationToken);
            try
            {
                // The blob cache may have dropped the file while the package waited for its turn.
                if (!File.Exists(path))
                    path = await SmtPackageTransfer.FetchAsync(BlobService, package, Logger, cancellationToken);

                SmtPackageRunner.EnsureBlob(package, path);

                // The module is compiled here at the first package of a process (tens of seconds),
                // before any session's budget starts: no other package is let in meanwhile.
                var sandbox = SmtSandboxHost.Get(out var unavailable);
                var degree = SmtPackageRunner.Prepare(package, sandbox == null ? unavailable ?? SANDBOX_MISSING : null, turn.Degree);

                Logger.LogInformation("Smt: {Problems} problem(s), up to {Degree} session(s) of theirs at once ({Reason}), on this node's budget beside {Open} other package(s) open",
                    package.ProblemCount, degree.Degree, degree.Reason, SmtSessionBudget.Node.Open - 1);

                var resultsPath = Path.Combine(work.Path, SmtPackageCodec.RESULTS_FILE_NAME);
                written = await SmtPackageRunner.RunAsync(turn, package, path, SmtPackageRunner.Solver(sandbox!, Logger), resultsPath, () =>
                {
                    Interlocked.Increment(ref solved);
                    progress.ProblemDone();
                }, Logger, cancellationToken);
            }
            finally
            {
                turn.Dispose();
            }

            return await SmtPackageTransfer.UploadResultsAsync(BlobService, written.Path, package.Ticket, written.Rows, Logger, cancellationToken);
        }
        catch (SmtPackageException refused)
        {
            // The reason travels in fixed words; where on the node it lies does not.
            Logger.LogWarning("Smt: this node does not work on a package: {Code}: {Reason}", refused.Code, refused.Message);

            if (!received)
                progress.Received(package);

            progress.ProblemsDropped(package.ProblemCount - Volatile.Read(ref solved));
            return Fault(package, refused);
        }
        finally
        {
            progress.Leave();
        }
    }

    private static SmtResultPackageData Fault(SmtTaskPackageData package, SmtPackageException refused)
    {
        return new SmtResultPackageData
        {
            Ticket = SmtTickets.IsWellFormed(package.Ticket) ? package.Ticket : "",
            FaultCode = refused.Code,
            FaultMessage = SmtResultRows.Cut(refused.Message, SmtResultWords.MAX_MESSAGE_CHARACTERS)
        };
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: one parameter, the package's reference.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtSolvePackage CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference package)
                throw new ArgumentException("Parameter 'Package' must be a variable reference.");

            return new WitActivitySmtSolvePackage
            {
                Package = package
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
