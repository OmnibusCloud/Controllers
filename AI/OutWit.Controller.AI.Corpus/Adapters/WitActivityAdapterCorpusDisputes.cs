using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Logging;
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
            var ledger = await CorpusRounds.LedgerAsync(BlobService, pool, activity.Tasks, activity.Executions, activity.Repairs, activity.Repaired);

            if (ledger.PendingDisputes > 0)
                Logger.LogWarning("Corpus.Disputes: the copies of {Count} shard(s) disagree; each runs again on up to three machines.", ledger.PendingDisputes);

            return await CorpusRounds.PackagesAsync(BlobService, ledger.EnumerateDisputeTasks(), ledger.PendingDisputes);
        });

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusDisputes CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 4)
                throw new ArgumentException($"Expected 4 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference tasks)
                throw new ArgumentException("Parameter 'Tasks' must be a variable reference.");

            if (parameters[1] is not IWitReference executions)
                throw new ArgumentException("Parameter 'Executions' must be a variable reference.");

            if (parameters[2] is not IWitReference repairs)
                throw new ArgumentException("Parameter 'Repairs' must be a variable reference.");

            if (parameters[3] is not IWitReference repaired)
                throw new ArgumentException("Parameter 'Repaired' must be a variable reference.");

            return new WitActivityCorpusDisputes
            {
                Tasks = tasks,
                Executions = executions,
                Repairs = repairs,
                Repaired = repaired
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
