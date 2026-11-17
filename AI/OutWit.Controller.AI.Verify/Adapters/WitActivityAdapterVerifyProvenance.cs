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

internal sealed class WitActivityAdapterVerifyProvenance : WitActivityAdapterFunction<WitActivityVerifyProvenance>
{
    #region Constants

    private const string BLOB_NAME = "verify-provenance.json";

    #endregion

    #region Constructors

    public WitActivityAdapterVerifyProvenance(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyProvenance activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var ledgerPath = await BlobService.GetLocalPathAsync(VerifyAdapterTools.Blob(pool, activity.Ledger, "Ledger"));
        var blobId = await VerifyAdapterTools.UploadDocumentAsync(BlobService, VerifyAdapterTools.WorkFolder(Services), ledgerPath, VerifyLedger.PROVENANCE, BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyProvenance CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            return new WitActivityVerifyProvenance
            {
                Ledger = VerifyAdapterTools.Reference(parameters, 0, "Ledger")
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
