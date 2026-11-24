using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// <c>Smt.Disputes</c> on the host: copies that disagree are not chosen between, and a model the
/// evaluator calls failed accuses nobody: the problem goes to three more machines.
/// </summary>
internal sealed class WitActivityAdapterSmtDisputes : WitActivityAdapterFunction<WitActivitySmtDisputes>
{
    #region Constants

    private const string WORK_LABEL = "smt-disputes";

    #endregion

    #region Constructors

    /// <summary>Makes the adapter.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="blobService">The host's blob service.</param>
    /// <param name="services">The host's services.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtDisputes(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    /// <summary>Stores the packages of the problems that go to three more machines.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A task that ends when the packages are stored.</returns>
    protected override async Task Process(WitActivitySmtDisputes activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var issued = SmtAdapterTools.TaskPackages(pool, activity.Packages, "Packages");
        var returned = SmtAdapterTools.ResultPackages(pool, activity.Results, "Results");

        using var work = new SmtWorkFolder(Services, WORK_LABEL);
        using var spool = new SmtSpool(work.Path);

        var main = await SmtSettlement.ReadMainAsync(issued, returned, BlobService, spool);
        var repair = activity.Repairs == null
            ? null
            : await SmtSettlement.ReadRepairAsync(SmtAdapterTools.TaskPackages(pool, activity.Repairs, "Repairs"), SmtAdapterTools.ResultPackages(pool, activity.Repaired, "Repaired"),
                BlobService, spool);

        var (packages, causes) = await SmtSettlement.PublishDisputesAsync(main, repair, spool, BlobService, work.Path);

        Logger.LogInformation("Smt.Disputes: {Problems} problem(s) go to three more machines in {Packages} package(s) ({Causes})",
            packages.Sum(package => (long)package.ProblemCount), packages.Count, string.Join(", ", causes.Select(pair => $"{pair.Key} {pair.Value}")));

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: the first fan-out, and the repair round when it is given.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtDisputes CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2 && parameters.Length != 4)
                throw new ArgumentException($"Expected 2 or 4 parameter(s), got {parameters.Length}.");

            return new WitActivitySmtDisputes
            {
                Packages = SmtAdapterTools.Reference(parameters, 0, "Packages"),
                Results = SmtAdapterTools.Reference(parameters, 1, "Results"),
                Repairs = parameters.Length > 2 ? SmtAdapterTools.Reference(parameters, 2, "Repairs") : null,
                Repaired = parameters.Length > 3 ? SmtAdapterTools.Reference(parameters, 3, "Repaired") : null
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
