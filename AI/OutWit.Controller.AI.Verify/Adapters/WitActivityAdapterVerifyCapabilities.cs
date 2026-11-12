using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifyCapabilities : WitActivityAdapterFunction<WitActivityVerifyCapabilities>
{
    #region Constants

    private const string BLOB_NAME = "verify-capabilities.json";

    #endregion

    #region Constructors

    public WitActivityAdapterVerifyCapabilities(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityVerifyCapabilities activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var diagnostics = VerifyRuntimeDiagnosticsBuilder.Build();
        var summary = await SummarizePoolAsync();

        var bytes = VerifyCapabilitiesWriter.Write(diagnostics, summary);
        var blobId = await BlobService.UploadBytesAsync(bytes, BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Tools

    private async Task<VerifyPoolSummary?> SummarizePoolAsync()
    {
        // The nodes manager exists on a host only; the module is loaded on nodes too, where
        // this adapter must still construct. A host that cannot answer leaves the pool out.
        try
        {
            return await VerifyPoolProbe.SummarizeAsync(Services.GetService<IWitNodesManager>());
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Verify.Capabilities: the pool could not be summarized; the document is written without it.");
            return null;
        }
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifyCapabilities CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 0)
            throw new ArgumentException($"Expected 0 parameter(s), got {parameters.Length}.");

        return new WitActivityVerifyCapabilities();
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    private IServiceProvider Services { get; }

    #endregion
}
