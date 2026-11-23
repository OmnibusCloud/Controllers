using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary><c>Smt.Provenance</c> on the host: the provenance of a settled run (<c>docs/smt-provenance.schema.json</c>), as a blob taken out of the run's ledger. It reads no node's row and decides nothing.</summary>
internal sealed class WitActivityAdapterSmtProvenance : WitActivityAdapterFunction<WitActivitySmtProvenance>
{
    #region Constants

    private const string BLOB_NAME = "smt-provenance.json";

    #endregion

    #region Constructors

    /// <summary>Makes the adapter.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="blobService">The host's blob service.</param>
    /// <param name="services">The host's services.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtProvenance(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    /// <summary>Stores the section as a blob of its own.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A task that ends when the blob is stored.</returns>
    protected override async Task Process(WitActivitySmtProvenance activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var ledgerPath = await BlobService.GetLocalPathAsync(SmtAdapterTools.Blob(pool, activity.Ledger, "Ledger"));

        // Written when the run was settled; handed out as it is.
        var blobId = await SmtAdapterTools.UploadSectionAsync(BlobService, Services, ledgerPath, SmtLedger.PROVENANCE, BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: the ledger.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtProvenance CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            return new WitActivitySmtProvenance { Ledger = SmtAdapterTools.Reference(parameters, 0, "Ledger") };
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
