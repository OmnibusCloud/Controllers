using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusCapabilities : WitActivityAdapterFunction<WitActivityCorpusCapabilities>
{
    #region Constants

    private const string BLOB_NAME = "corpus-capabilities.json";

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusCapabilities(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusCapabilities activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var diagnostics = await Task.Run(() => CorpusRuntimeDiagnosticsBuilder.Build(cancellationToken: cancellationToken), cancellationToken);
        var summary = await SummarizePoolAsync();
        var limit = await new CorpusStorageHeadroomHost(BlobService).GetAsync(cancellationToken);

        var bytes = CorpusCapabilitiesWriter.Write(diagnostics, summary, limit);
        var blobId = await BlobService.UploadBytesAsync(bytes, BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Tools

    private async Task<CorpusPoolSummary?> SummarizePoolAsync()
    {
        // The nodes manager exists on a host only; the module is loaded on nodes too, where
        // this adapter must still construct. A host that cannot answer leaves the pool out.
        try
        {
            return await CorpusPoolProbe.SummarizeAsync(Services.GetService<IWitNodesManager>());
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Corpus.Capabilities: the pool could not be summarized; the document is written without it.");
            return null;
        }
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusCapabilities CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 0)
            throw new ArgumentException($"Expected 0 parameter(s), got {parameters.Length}.");

        return new WitActivityCorpusCapabilities();
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    private IServiceProvider Services { get; }

    #endregion
}
