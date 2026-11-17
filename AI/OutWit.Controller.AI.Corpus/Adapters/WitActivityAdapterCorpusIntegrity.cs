using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusIntegrity : WitActivityAdapterFunction<WitActivityCorpusIntegrity>
{
    #region Constants

    private const string BLOB_NAME = "corpus-integrity.json";

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusIntegrity(IWitProcessingManager processingManager, IWitBlobService blobService, IWitTempStorage tempStorage, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        TempStorage = tempStorage;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusIntegrity activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var blobId = await CorpusRounds.HostAsync("Corpus.Integrity", async () =>
        {
            var ledger = await CorpusRounds.LedgerAsync(BlobService, pool, activity.Tasks, activity.Executions, activity.Repairs, activity.Repaired, activity.Disputes, activity.Resolved);

            // The pairs grow with the manifest (a share of its shards): written as they are read.
            return await CorpusDocuments.UploadAsync(BlobService, TempStorage, BLOB_NAME, stream => CorpusIntegrityWriter.Write(ledger, stream));
        });

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusIntegrity CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 6)
                throw new ArgumentException($"Expected 6 parameter(s), got {parameters.Length}.");

            var references = new IWitReference[6];
            string[] names = ["Tasks", "Executions", "Repairs", "Repaired", "Disputes", "Resolved"];
            for (var index = 0; index < references.Length; index++)
                references[index] = parameters[index] as IWitReference ?? throw new ArgumentException($"Parameter '{names[index]}' must be a variable reference.");

            return new WitActivityCorpusIntegrity
            {
                Tasks = references[0],
                Executions = references[1],
                Repairs = references[2],
                Repaired = references[3],
                Disputes = references[4],
                Resolved = references[5]
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
