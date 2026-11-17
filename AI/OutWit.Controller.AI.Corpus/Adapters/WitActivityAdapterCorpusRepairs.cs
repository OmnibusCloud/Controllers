using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusRepairs : WitActivityAdapterFunction<WitActivityCorpusRepairs>
{
    #region Constructors

    public WitActivityAdapterCorpusRepairs(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override Task Process(WitActivityCorpusRepairs activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var ledger = CorpusRounds.Ledger(pool, activity.Tasks, activity.Executions);
        var tasks = ledger.RepairTasks();

        if (tasks.Count > 0)
            Logger.LogWarning("Corpus.Repairs: {Count} shard(s) came back without a result ({Faults} node fault(s), {Rejected} value(s) dropped); they are sent out once more.", tasks.Count, ledger.NodeFaults, ledger.RejectedResults);

        if (!pool.TrySetCollection(activity.ReturnReference, tasks))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
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
}
