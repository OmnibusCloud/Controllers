using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Diagnostics;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary><c>Smt.Capabilities</c>: the capabilities document as a blob (<see cref="SmtCapabilitiesWriter"/>).</summary>
internal sealed class WitActivityAdapterSmtCapabilities : WitActivityAdapterFunction<WitActivitySmtCapabilities>
{
    #region Constants

    private const string BLOB_NAME = "smt-capabilities.json";

    #endregion

    #region Constructors

    /// <summary>Makes the adapter of a host or a node.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="blobService">The host's blob service.</param>
    /// <param name="services">The host's services: its nodes manager, when it is a host.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtCapabilities(IWitProcessingManager processingManager, IWitBlobService blobService, IServiceProvider services, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
        BlobService = blobService;
        Services = services;
    }

    #endregion

    #region Processing

    /// <summary>Writes the document, stores it as a blob and sets the blob as the return value.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A task that ends when the blob is stored.</returns>
    protected override async Task Process(WitActivitySmtCapabilities activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var diagnostics = SmtRuntimeDiagnosticsBuilder.Build();
        var summary = await SummarizePoolAsync();

        var bytes = SmtCapabilitiesWriter.Write(diagnostics, summary);
        var blobId = await BlobService.UploadBytesAsync(bytes, BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Tools

    private async Task<SmtPoolSummary?> SummarizePoolAsync()
    {
        // The nodes manager exists on a host only; the module is loaded on nodes too, where this
        // adapter must still construct. A host that cannot answer leaves the pool out.
        try
        {
            return await SmtPoolProbe.SummarizeAsync(Services.GetService<IWitNodesManager>());
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Smt.Capabilities: the pool could not be summarized; the document is written without it.");
            return null;
        }
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: no parameter.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtCapabilities CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 0)
            throw new ArgumentException($"Expected 0 parameter(s), got {parameters.Length}.");

        return new WitActivitySmtCapabilities();
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    private IServiceProvider Services { get; }

    #endregion
}
