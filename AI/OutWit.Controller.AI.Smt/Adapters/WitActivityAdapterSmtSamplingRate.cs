using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary><c>Smt.SamplingRate</c> on the host: the options' rate, 0 to 1, as the number Grid.Replicas takes.</summary>
internal sealed class WitActivityAdapterSmtSamplingRate : WitActivityAdapterFunction<WitActivitySmtSamplingRate>
{
    #region Constructors

    /// <summary>Makes the adapter.</summary>
    /// <param name="processingManager">The host's processing manager.</param>
    /// <param name="logger">The host's logger.</param>
    public WitActivityAdapterSmtSamplingRate(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, SmtLoggerGuarded.Wrap(logger))
    {
    }

    #endregion

    #region Processing

    /// <summary>Gives the rate.</summary>
    /// <param name="activity">The activity.</param>
    /// <param name="pool">The job's variables.</param>
    /// <param name="activityStatus">The activity's status, or null.</param>
    /// <param name="status">The processing status.</param>
    /// <returns>A finished task.</returns>
    protected override Task Process(WitActivitySmtSamplingRate activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        var rate = SmtSampling.Clamp(SmtAdapterTools.Options(pool, activity.Options).SamplingRate);

        if (!pool.TrySetValue(activity.ReturnReference, rate))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    /// <summary>Makes the activity of a script's call: the options.</summary>
    /// <param name="parameters">The call's parameters.</param>
    /// <returns>The activity.</returns>
    protected override WitActivitySmtSamplingRate CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            return new WitActivitySmtSamplingRate { Options = SmtAdapterTools.Reference(parameters, 0, "Options") };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion
}
