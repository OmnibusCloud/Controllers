using System.Collections;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusSample : WitActivityAdapterFunction<WitActivityCorpusSample>
{
    #region Constructors

    public WitActivityAdapterCorpusSample(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override Task Process(WitActivityCorpusSample activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetObject(activity.Tasks, out var tasksObject) || tasksObject is not IEnumerable tasksEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Tasks'.");

        pool.TryGetValue(activity.Options, out CorpusOptionsData? options);

        var tasks = tasksEnumerable.OfType<CorpusTaskData>().ToList();
        var replicas = CorpusSampler.Sample(tasks, options);

        Logger.LogInformation("Corpus.Sample: {Replicas} replica task(s) of {Tasks} at rate {Rate}", replicas.Count, tasks.Count, options?.SamplingRate ?? 0);

        if (!pool.TrySetCollection(activity.ReturnReference, replicas))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusSample CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference tasks)
                throw new ArgumentException("Parameter 'Tasks' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityCorpusSample
            {
                Tasks = tasks,
                Options = options
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
