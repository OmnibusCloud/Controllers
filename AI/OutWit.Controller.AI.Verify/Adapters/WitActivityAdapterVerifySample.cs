using System.Collections;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

internal sealed class WitActivityAdapterVerifySample : WitActivityAdapterFunction<WitActivityVerifySample>
{
    #region Constructors

    public WitActivityAdapterVerifySample(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override Task Process(WitActivityVerifySample activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetObject(activity.Batches, out var batchesObject) || batchesObject is not IEnumerable batchesEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Batches'.");

        pool.TryGetValue(activity.Options, out VerifyOptionsData? options);

        var batches = batchesEnumerable.OfType<VerifyTaskBatchData>().ToList();
        var replicas = VerifySampler.Sample(batches, options);

        Logger.LogInformation("Verify.Sample: {Tasks} task(s) in {Batches} replica batch(es) at rate {Rate}",
            replicas.Sum(batch => batch.Tasks.Count), replicas.Count, options?.SamplingRate ?? 0);

        if (!pool.TrySetCollection(activity.ReturnReference, replicas))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivityVerifySample CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference batches)
                throw new ArgumentException("Parameter 'Batches' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityVerifySample
            {
                Batches = batches,
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
