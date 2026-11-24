using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// <c>Smt.Repairs</c> on the host: what the first fan-out returned is taken in under its tickets,
/// and what has no answer, or fewer than the two answers its package was asked for, goes out once
/// more (<see cref="SmtRulings.NeedsRepair"/>).
/// </summary>
internal sealed class WitActivityAdapterSmtRepairs : WitActivityAdapterFunction<WitActivitySmtRepairs>
{
    #region Constants

    private const string WORK_LABEL = "smt-repairs";

    #endregion

    #region Constructors

    /// <summary>Makes the adapter.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="blobService">The host's blob service.</param>
    /// <param name="services">The host's services.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtRepairs(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    /// <summary>Stores the packages of the problems without an answer.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A task that ends when the packages are stored.</returns>
    protected override async Task Process(WitActivitySmtRepairs activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var issued = SmtAdapterTools.TaskPackages(pool, activity.Packages, "Packages");
        var returned = SmtAdapterTools.ResultPackages(pool, activity.Results, "Results");

        double? rate = null;
        if (activity.Rate != null)
        {
            if (!pool.TryGetValue(activity.Rate, out double stated))
                throw new InvalidOperationException("Failed to get parameter 'Rate'.");

            rate = SmtSampling.Clamp(stated);
        }

        using var work = new SmtWorkFolder(Services, WORK_LABEL);
        var main = await SmtSettlement.ReadMainAsync(issued, returned, BlobService);
        var packages = await SmtSettlement.PublishRepairsAsync(main, rate, BlobService, work.Path);

        Logger.LogInformation("Smt.Repairs: {Problems} problem(s) without an answer or short of the copies asked for go out once more, to two machines, in {Packages} package(s); {Rejected} of {Returned} result package(s) refused, {Faults} came back with a node's fault code",
            packages.Sum(package => (long)package.ProblemCount), packages.Count, main.RejectedTotal, returned.Count, main.Faults.Values.Sum());

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: the packages, what came back, and the rate when it is given.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtRepairs CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2 && parameters.Length != 3)
                throw new ArgumentException($"Expected 2 or 3 parameter(s), got {parameters.Length}.");

            return new WitActivitySmtRepairs
            {
                Packages = SmtAdapterTools.Reference(parameters, 0, "Packages"),
                Results = SmtAdapterTools.Reference(parameters, 1, "Results"),
                Rate = parameters.Length > 2 ? SmtAdapterTools.Reference(parameters, 2, "Rate") : null
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
