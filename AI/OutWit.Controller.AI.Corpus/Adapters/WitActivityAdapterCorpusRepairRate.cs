using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Logging;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusRepairRate : WitActivityAdapterFunction<WitActivityCorpusRepairRate>
{
    #region Constructors

    public WitActivityAdapterCorpusRepairRate(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, CorpusLogger.Wrap(logger))
    {
    }

    #endregion

    #region Processing

    protected override Task Process(WitActivityCorpusRepairRate activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        pool.TryGetValue(activity.Options, out CorpusOptionsData? options);

        // A run that asked for sampling has every shard of its repair round run twice; a run
        // that asked for none is not given a second execution it did not ask for.
        var rate = CorpusSampling.RepairRateOf(options);

        if (!pool.TrySetValue(activity.ReturnReference, rate))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusRepairRate CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityCorpusRepairRate { Options = options };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion
}
