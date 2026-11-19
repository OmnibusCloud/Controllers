using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusRepairs : WitActivityAdapterFunction<WitActivityCorpusRepairs>
{
    #region Constructors

    public WitActivityAdapterCorpusRepairs(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, CorpusLogger.Wrap(logger))
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusRepairs activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var packages = await CorpusRounds.HostAsync("Corpus.Repairs", async () =>
        {
            var ledger = await CorpusRounds.LedgerAsync(BlobService, pool, activity.Tasks, activity.Executions);

            if (ledger.PendingRepairs > 0)
                Logger.LogWarning("Corpus.Repairs: {Count} shard(s) came back without a result ({Faults} node fault(s), {Rejected} value(s) dropped); they are sent out once more.", ledger.PendingRepairs, ledger.NodeFaults, ledger.RejectedResults);

            // A fan-out that came back short: packages nobody answered at all.
            if (ledger.FirstRound.Unanswered > 0)
                Logger.LogWarning("Corpus.Repairs: {Unanswered} of the {Packages} package(s) of the first round were not answered.", ledger.FirstRound.Unanswered, ledger.FirstRound.Packages);

            // The run is read once more, a shard at a time, and its tasks go straight into packages.
            return await CorpusRounds.PackagesAsync(BlobService, ledger.EnumerateRepairTasks(), ledger.PendingRepairs);
        });

        if (!pool.TrySetCollection(activity.ReturnReference, packages))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusRepairs CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference tasks)
                throw new ArgumentException("Parameter 'Tasks' must be a variable reference.");

            if (parameters[1] is not IWitReference executions)
                throw new ArgumentException("Parameter 'Executions' must be a variable reference.");

            return new WitActivityCorpusRepairs
            {
                Tasks = tasks,
                Executions = executions
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
