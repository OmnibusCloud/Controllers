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

internal sealed class WitActivityAdapterVerifyCollect : WitActivityAdapterFunction<WitActivityVerifyCollect>
{
    #region Constants

    private const string BLOB_NAME = "verify-report.json";

    #endregion

    #region Constructors

    public WitActivityAdapterVerifyCollect(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyCollect activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var ledgerPath = await BlobService.GetLocalPathAsync(VerifyAdapterTools.Blob(pool, activity.Ledger, "Ledger"));

        // The report was written when the run was settled; it is handed out here as it is.
        var reportBlobId = await BlobService.UploadBytesAsync(VerifyLedger.Read(ledgerPath, VerifyLedger.REPORT), BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, reportBlobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyCollect CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            return new WitActivityVerifyCollect
            {
                Ledger = VerifyAdapterTools.Reference(parameters, 0, "Ledger"),
                Options = VerifyAdapterTools.Reference(parameters, 1, "Options")
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
