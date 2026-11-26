using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// <c>Smt.Settle</c> on the host: the run is settled and its documents are written into one ledger
/// blob (<see cref="SmtRunSettlement"/>). A cancelled job ends it at the next package or row, cancelled,
/// and its spool and working folder go with it.
/// </summary>
internal sealed class WitActivityAdapterSmtSettle : WitActivityAdapterFunction<WitActivitySmtSettle>
{
    #region Constants

    private const string WORK_LABEL = "smt-settle";

    #endregion

    #region Constructors

    /// <summary>Makes the adapter.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="blobService">The host's blob service.</param>
    /// <param name="services">The host's services.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtSettle(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    /// <summary>Settles the run and stores its ledger.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A task that ends when the ledger is stored.</returns>
    protected override async Task Process(WitActivitySmtSettle activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var problemsPath = await BlobService.GetLocalPathAsync(SmtAdapterTools.Blob(pool, activity.Problems, "Problems"));
        var options = SmtAdapterTools.Options(pool, activity.Options);
        SmtAdapterTools.EnsureRunnable(options);

        var fanOut = (Issued: (IReadOnlyList<SmtTaskPackageData>)SmtAdapterTools.TaskPackages(pool, activity.Packages, "Packages"),
            Returned: (IEnumerable<SmtResultPackageData?>)SmtAdapterTools.ResultPackages(pool, activity.Results, "Results"));
        var repair = (Issued: (IReadOnlyList<SmtTaskPackageData>)SmtAdapterTools.TaskPackages(pool, activity.Repairs, "Repairs"),
            Returned: (IEnumerable<SmtResultPackageData?>)SmtAdapterTools.ResultPackages(pool, activity.Repaired, "Repaired"));

        // A script that never samples and never fails a model has nothing to dispute, and may run no
        // dispute round. The repair round asked two machines for every package.
        (IReadOnlyList<SmtTaskPackageData> Issued, IEnumerable<SmtResultPackageData?> Returned)? dispute = activity.Disputes == null
            ? null
            : (SmtAdapterTools.TaskPackages(pool, activity.Disputes, "Disputes"), SmtAdapterTools.ResultPackages(pool, activity.Settled, "Settled"));

        double? samplingRate = null;
        if (activity.Rate != null)
        {
            if (!pool.TryGetValue(activity.Rate, out double rate))
                throw new InvalidOperationException("Failed to get parameter 'Rate'.");

            samplingRate = rate;
        }

        using var work = new SmtWorkFolder(Services, WORK_LABEL);
        var (ledgerPath, facts) = await SmtRunSettlement.SettleAsync(problemsPath, options, fanOut, repair, dispute, BlobService, work.Path, samplingRate,
            cancellation: ProcessingManager.CancellationToken(status.JobId));
        try
        {
            Logger.LogInformation(
                "Smt.Settle: {Problems} problem(s); {Copies} answer(s) taken, {Disputes} dispute(s) ({Resolved} resolved), {Lies} lie(s) found, {Disagreements} evaluator/solver disagreement(s), {Repairs} repair(s), {NoResult} without a result; {Rejected} result package(s) refused; sampling {Sampling}; binding {Binding}",
                facts.Problems, facts.CopiesMade, facts.Disputes.Count, facts.Disputes.Count(dispute => dispute.Resolved), facts.Lies, facts.EvaluatorDisagreements.Count, facts.Repairs,
                facts.NoResult, facts.Rejected.Values.Sum(), facts.Sampling, facts.Binding);

            if (facts.EvaluatorDisagreements.Count > 0)
            {
                Logger.LogWarning("Smt.Settle: the machines agree and the evaluator calls the model failed on {Count} problem(s), the first at index {Index}: a disagreement between the evaluator and the solver",
                    facts.EvaluatorDisagreements.Count, facts.EvaluatorDisagreements[0].Index);
            }

            var ledgerBlobId = await BlobService.UploadFileAsync(ledgerPath);

            if (!pool.TrySetValue(activity.ReturnReference, ledgerBlobId))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
        }
        finally
        {
            SmtSettlement.TryDelete(ledgerPath);
        }
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: the set, the options, the rounds, and the rate when it is given.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtSettle CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 6 && parameters.Length != 8 && parameters.Length != 9)
                throw new ArgumentException($"Expected 6, 8 or 9 parameter(s), got {parameters.Length}.");

            return new WitActivitySmtSettle
            {
                Problems = SmtAdapterTools.Reference(parameters, 0, "Problems"),
                Options = SmtAdapterTools.Reference(parameters, 1, "Options"),
                Packages = SmtAdapterTools.Reference(parameters, 2, "Packages"),
                Results = SmtAdapterTools.Reference(parameters, 3, "Results"),
                Repairs = SmtAdapterTools.Reference(parameters, 4, "Repairs"),
                Repaired = SmtAdapterTools.Reference(parameters, 5, "Repaired"),
                Disputes = parameters.Length > 6 ? SmtAdapterTools.Reference(parameters, 6, "Disputes") : null,
                Settled = parameters.Length > 7 ? SmtAdapterTools.Reference(parameters, 7, "Settled") : null,
                Rate = parameters.Length > 8 ? SmtAdapterTools.Reference(parameters, 8, "Rate") : null
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
