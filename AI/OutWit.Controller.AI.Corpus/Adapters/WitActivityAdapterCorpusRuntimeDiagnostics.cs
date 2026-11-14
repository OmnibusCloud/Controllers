using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusRuntimeDiagnostics : WitActivityAdapterFunction<WitActivityCorpusRuntimeDiagnostics>
{
    #region Constructors

    public WitActivityAdapterCorpusRuntimeDiagnostics(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusRuntimeDiagnostics activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);
        var diagnostics = await Task.Run(() => CorpusRuntimeDiagnosticsBuilder.Build(cancellationToken: cancellationToken), cancellationToken);

        if (!diagnostics.StagesVerified)
            Logger.LogWarning("Corpus.RuntimeDiagnostics: the reference shard hashes to {Actual} here, not the pinned {Pinned}", diagnostics.StageSetSha256, CorpusStageSet.PINNED_SHA256);

        if (!pool.TrySetValue(activity.ReturnReference, diagnostics))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusRuntimeDiagnostics CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 0)
            throw new ArgumentException($"Expected 0 parameter(s), got {parameters.Length}.");

        return new WitActivityCorpusRuntimeDiagnostics();
    }

    #endregion
}
