using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusCollect : WitActivityAdapterFunction<WitActivityCorpusCollect>
{
    #region Constants

    private const string BLOB_NAME = "corpus-report.json";

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusCollect(IWitProcessingManager processingManager, IWitBlobService blobService, IWitTempStorage tempStorage, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        TempStorage = tempStorage;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusCollect activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var reportBlobId = await CorpusRounds.HostAsync("Corpus.Collect", async () =>
        {
            // The task list is the server's own: the report holds every shard of it, bound to what
            // came back by the tickets, whatever order and whatever indexes the nodes returned.
            var ledger = await CorpusRounds.LedgerAsync(BlobService, pool, activity.Tasks, activity.Executions, activity.Repairs, activity.Repaired, activity.Disputes, activity.Resolved);

            if (ledger.RejectedResults > 0 || ledger.Disputes > 0)
                Logger.LogWarning("Corpus.Collect: {Rejected} value(s) dropped, {Disputes} dispute(s) of which {Resolved} settled.", ledger.RejectedResults, ledger.Disputes, ledger.DisputesResolved);

            // Written to a file a shard at a time and uploaded as that file: the report grows
            // with the manifest and is never held.
            return await CorpusDocuments.UploadAsync(BlobService, TempStorage, BLOB_NAME, stream => CorpusReportWriter.Write(ledger, stream));
        });

        if (!pool.TrySetValue(activity.ReturnReference, reportBlobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusCollect CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 7)
                throw new ArgumentException($"Expected 7 parameter(s), got {parameters.Length}.");

            var references = new IWitReference[7];
            string[] names = ["Tasks", "Executions", "Repairs", "Repaired", "Disputes", "Resolved", "Options"];
            for (var index = 0; index < references.Length; index++)
                references[index] = parameters[index] as IWitReference ?? throw new ArgumentException($"Parameter '{names[index]}' must be a variable reference.");

            return new WitActivityCorpusCollect
            {
                Tasks = references[0],
                Executions = references[1],
                Repairs = references[2],
                Repaired = references[3],
                Disputes = references[4],
                Resolved = references[5],
                Options = references[6]
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

    private IWitTempStorage TempStorage { get; }

    #endregion
}
