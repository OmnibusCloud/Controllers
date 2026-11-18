using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifySettle : WitActivityAdapterFunction<WitActivityVerifySettle>
{
    #region Constructors

    public WitActivityAdapterVerifySettle(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, VerifyLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifySettle activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var fanOut = (Issued: (IReadOnlyList<VerifyTaskPackageData>)VerifyAdapterTools.TaskPackages(pool, activity.Packages, "Packages"),
            Returned: (IEnumerable<VerifyResultPackageData>)VerifyAdapterTools.ResultPackages(pool, activity.Results, "Results"));
        var repair = (Issued: (IReadOnlyList<VerifyTaskPackageData>)VerifyAdapterTools.TaskPackages(pool, activity.Repairs, "Repairs"),
            Returned: (IEnumerable<VerifyResultPackageData>)VerifyAdapterTools.ResultPackages(pool, activity.Repaired, "Repaired"));

        // A script that never samples has no executions to disagree, and runs no dispute round.
        (IReadOnlyList<VerifyTaskPackageData> Issued, IEnumerable<VerifyResultPackageData> Returned)? dispute = activity.Disputes == null
            ? null
            : (VerifyAdapterTools.TaskPackages(pool, activity.Disputes, "Disputes"), VerifyAdapterTools.ResultPackages(pool, activity.Settled, "Settled"));

        using var work = new VerifyWorkFolder(Services);
        var (ledgerPath, summary) = await VerifySettlement.SettleAsync(fanOut, repair, dispute, BlobService, work.Path);
        try
        {
            Logger.LogInformation(
                "Verify.Settle: {Total} task(s), {Pass} pass, {NoResult} without a result, {Disputed} disputed; {Rejected} result package(s) refused, {Disputes} dispute(s), {Resolved} resolved",
                summary.Total, summary.Pass, summary.Verdicts.GetValueOrDefault(VerifyVerdict.NoResult), summary.Verdicts.GetValueOrDefault(VerifyVerdict.Disputed),
                summary.RejectedResults, summary.Disputes, summary.DisputesResolved);

            var ledgerBlobId = await BlobService.UploadFileAsync(ledgerPath);

            if (!pool.TrySetValue(activity.ReturnReference, ledgerBlobId))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
        }
        finally
        {
            VerifySettlement.TryDelete(ledgerPath);
        }
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifySettle CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 4 && parameters.Length != 6)
                throw new ArgumentException($"Expected 4 or 6 parameter(s), got {parameters.Length}.");

            return new WitActivityVerifySettle
            {
                Packages = VerifyAdapterTools.Reference(parameters, 0, "Packages"),
                Results = VerifyAdapterTools.Reference(parameters, 1, "Results"),
                Repairs = VerifyAdapterTools.Reference(parameters, 2, "Repairs"),
                Repaired = VerifyAdapterTools.Reference(parameters, 3, "Repaired"),
                Disputes = parameters.Length > 4 ? VerifyAdapterTools.Reference(parameters, 4, "Disputes") : null,
                Settled = parameters.Length > 5 ? VerifyAdapterTools.Reference(parameters, 5, "Settled") : null
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
