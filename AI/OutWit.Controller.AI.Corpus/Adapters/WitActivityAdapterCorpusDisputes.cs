using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusDisputes : WitActivityAdapterFunction<WitActivityCorpusDisputes>
{
    #region Constructors

    public WitActivityAdapterCorpusDisputes(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, CorpusLogger.Wrap(logger))
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusDisputes activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var packages = await CorpusRounds.HostAsync("Corpus.Disputes", async () =>
        {
            // A dispute round that sent nothing out leaves nothing to send out again: the run is not read.
            if (activity.Disputes != null && IsEmpty(pool, activity.Disputes))
                return new List<CorpusTaskPackageData>();

            var ledger = await CorpusRounds.LedgerAsync(BlobService, pool, activity.Tasks, activity.Executions, activity.Repairs, activity.Repaired, activity.Disputes, activity.Resolved);

            // Given the dispute round, the disputes it could not settle for want of copies go out once more.
            if (activity.Disputes != null)
            {
                if (ledger.PendingDisputesAgain > 0)
                    Logger.LogWarning("Corpus.Disputes: {Count} dispute(s) came back with too few copies, or with a lead that may be one machine's; each runs once more on up to three machines.", ledger.PendingDisputesAgain);

                return await CorpusRounds.PackagesAsync(BlobService, ledger.EnumerateDisputeAgainTasks(), ledger.PendingDisputesAgain);
            }

            if (ledger.PendingDisputes > 0)
                Logger.LogWarning("Corpus.Disputes: the copies of {Count} shard(s) disagree; each runs again on up to three machines.", ledger.PendingDisputes);

            return await CorpusRounds.PackagesAsync(BlobService, ledger.EnumerateDisputeTasks(), ledger.PendingDisputes);
        });

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Tools

    /// <summary>Whether a round's task collection holds no package.</summary>
    private static bool IsEmpty(IWitVariablesCollection pool, IWitReference reference)
    {
        return pool.TryGetObject(reference, out var value) && value is System.Collections.ICollection { Count: 0 };
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusDisputes CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            // Four arguments name the rounds before the dispute round; six name the dispute round
            // too, and ask for its disputes to go out once more.
            if (parameters.Length is not (4 or 6))
                throw new ArgumentException($"Expected 4 or 6 parameter(s), got {parameters.Length}.");

            var references = new IWitReference[parameters.Length];
            string[] names = ["Tasks", "Executions", "Repairs", "Repaired", "Disputes", "Resolved"];
            for (var index = 0; index < references.Length; index++)
                references[index] = parameters[index] as IWitReference ?? throw new ArgumentException($"Parameter '{names[index]}' must be a variable reference.");

            return new WitActivityCorpusDisputes
            {
                Tasks = references[0],
                Executions = references[1],
                Repairs = references[2],
                Repaired = references[3],
                Disputes = references.Length > 4 ? references[4] : null,
                Resolved = references.Length > 4 ? references[5] : null
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

    #endregion
}
