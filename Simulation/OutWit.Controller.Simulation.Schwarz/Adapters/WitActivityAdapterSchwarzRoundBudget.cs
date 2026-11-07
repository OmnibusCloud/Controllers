using Microsoft.Extensions.Logging;
using OutWit.Math.Simulation;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Math.Simulation.Model.Schwarz;

namespace OutWit.Controller.Simulation.Schwarz.Adapters;

internal sealed class WitActivityAdapterSchwarzRoundBudget : WitActivityAdapterFunction<WitActivitySchwarzRoundBudget>
{
    #region Constructors

    public WitActivityAdapterSchwarzRoundBudget(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySchwarzRoundBudget activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Options, out SchwarzOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get parameter 'Options'.");

        if (!pool.TrySetValue(activity.ReturnReference, options.MaxRounds))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        await Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivitySchwarzRoundBudget CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivitySchwarzRoundBudget
            {
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
