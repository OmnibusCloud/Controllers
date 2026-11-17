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

internal sealed class WitActivityAdapterVerifyCollectOutputs : WitActivityAdapterFunction<WitActivityVerifyCollectOutputs>
{
    #region Constants

    private const string BLOB_NAME = "verify-corpus.jsonl";

    #endregion

    #region Constructors

    public WitActivityAdapterVerifyCollectOutputs(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyCollectOutputs activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var ledgerPath = await BlobService.GetLocalPathAsync(VerifyAdapterTools.Blob(pool, activity.Ledger, "Ledger"));

        // The outputs can be as large as the run: they go from the ledger to a file and from the
        // file to the blob store, never through memory as a whole.
        var blobId = await VerifyAdapterTools.UploadDocumentAsync(BlobService, VerifyAdapterTools.WorkFolder(Services), ledgerPath, VerifyLedger.OUTPUTS, BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyCollectOutputs CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            return new WitActivityVerifyCollectOutputs
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

    private IServiceProvider Services { get; }

    #endregion
}
