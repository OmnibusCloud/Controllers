using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusIntegrity : WitActivityAdapterFunction<WitActivityCorpusIntegrity>
{
    #region Constants

    private const string BLOB_NAME = "corpus-integrity.json";

    /// <summary>The arguments that name the rounds: the tasks and the answers of each of the three.</summary>
    private const int ROUND_PARAMETERS = 6;

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusIntegrity(IWitProcessingManager processingManager, IWitBlobService blobService, IWitTempStorage tempStorage, ILogger logger)
        : base(processingManager, CorpusLogger.Wrap(logger))
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
            var ledger = await CorpusRounds.LedgerAsync(BlobService, pool, activity.Tasks, activity.Executions, activity.Repairs, activity.Repaired, activity.Disputes, activity.Resolved, activity.Options);

            if (ledger.Sampling.State is CorpusSampling.STATE_SHORT or CorpusSampling.STATE_NONE)
                Logger.LogWarning("Corpus.Integrity: {Statement}", ledger.Sampling.Statement);

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
            // The run's options are the last argument since 0.7.0; a script written for an earlier
            // controller hands six, and its document says the sampling asked for is not known.
            if (parameters.Length is not (ROUND_PARAMETERS or ROUND_PARAMETERS + 1))
                throw new ArgumentException($"Expected {ROUND_PARAMETERS} or {ROUND_PARAMETERS + 1} parameter(s), got {parameters.Length}.");

            var references = new IWitReference[parameters.Length];
            string[] names = ["Tasks", "Executions", "Repairs", "Repaired", "Disputes", "Resolved", "Options"];
            for (var index = 0; index < references.Length; index++)
                references[index] = parameters[index] as IWitReference ?? throw new ArgumentException($"Parameter '{names[index]}' must be a variable reference.");

            return new WitActivityCorpusIntegrity
            {
                Tasks = references[0],
                Executions = references[1],
                Repairs = references[2],
                Repaired = references[3],
                Disputes = references[4],
                Resolved = references[5],
                Options = references.Length > ROUND_PARAMETERS ? references[ROUND_PARAMETERS] : null
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
