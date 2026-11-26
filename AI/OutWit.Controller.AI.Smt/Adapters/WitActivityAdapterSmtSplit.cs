using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Planning;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// <c>Smt.Split</c> on the host: the options are held to their ranges and to the controller's
/// profile, the problem set is read a row at a time and its taken problems are kept in a working
/// file, and they are cut into packages by the planner's rule and stored as blobs. A set without a
/// row is refused; a set whose every row is refused gives no package, and its results say why.
/// </summary>
internal sealed class WitActivityAdapterSmtSplit : WitActivityAdapterFunction<WitActivitySmtSplit>
{
    #region Constants

    private const string WORK_LABEL = "smt-split";
    private const string SPOOL_FOLDER = "problems";
    private const string PACKAGES_FOLDER = "packages";

    #endregion

    #region Constructors

    /// <summary>Makes the adapter.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="blobService">The host's blob service.</param>
    /// <param name="services">The host's services.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtSplit(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    /// <summary>Splits the set the activity names into packages of problems.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A task that ends when every package is stored.</returns>
    protected override async Task Process(WitActivitySmtSplit activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var problemsPath = await BlobService.GetLocalPathAsync(SmtAdapterTools.Blob(pool, activity.Problems, "Problems"));
        var options = SmtAdapterTools.Options(pool, activity.Options);
        SmtAdapterTools.EnsureRunnable(options);

        // The set is read a row at a time and its taken problems wait in a working file of the
        // host's own until the run knows how many it has: memory holds a row and a package. A
        // cancelled job ends the split at the next row or problem, and the working folder goes
        // with it.
        var cancellation = ProcessingManager.CancellationToken(status.JobId);
        using var work = new SmtWorkFolder(Services, WORK_LABEL);
        using var spool = new SmtProblemSpool(Path.Combine(work.Path, SPOOL_FOLDER));

        var scan = SmtProblemSetScan.Read(problemsPath, options, spool, cancellation);
        if (scan.Summary.Rows == 0)
            throw new InvalidOperationException("The problem set has no row.");

        var publisher = new SmtPackagePublisher(spool.Count, SmtOptionsRules.Resolve(options), BlobService, Path.Combine(work.Path, PACKAGES_FOLDER));
        foreach (var problem in spool.ReadAll())
        {
            cancellation.ThrowIfCancellationRequested();
            await publisher.AddAsync(problem);
        }

        var packages = await publisher.CompleteAsync();

        Logger.LogInformation("Smt.Split: {Rows} row(s), {Accepted} taken, {Refused} refused; {Packages} package(s) of at most {PerPackage} problem(s), {Bytes} bytes",
            scan.Summary.Rows, scan.Summary.Accepted, scan.Summary.Refused, packages.Count, SmtPackagePlanner.ProblemsPerPackage(spool.Count), publisher.Bytes);

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: the problem set and the options.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtSplit CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            return new WitActivitySmtSplit
            {
                Problems = SmtAdapterTools.Reference(parameters, 0, "Problems"),
                Options = SmtAdapterTools.Reference(parameters, 1, "Options")
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
