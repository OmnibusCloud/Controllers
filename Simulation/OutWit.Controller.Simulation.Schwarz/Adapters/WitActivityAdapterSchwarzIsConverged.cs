using Microsoft.Extensions.Logging;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Adapters;

internal sealed class WitActivityAdapterSchwarzIsConverged : WitActivityAdapterFunction<WitActivitySchwarzIsConverged>
{
    #region Constructors

    public WitActivityAdapterSchwarzIsConverged(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySchwarzIsConverged activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.State, out SchwarzRoundData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        var converged = state.Round > 0
                        && state.Residual <= state.Eps * state.InitialResidual;

        if (!pool.TrySetValue(activity.ReturnReference, converged))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        await Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivitySchwarzIsConverged CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference state)
                throw new ArgumentException("Parameter 'State' must be a variable reference.");

            return new WitActivitySchwarzIsConverged
            {
                State = state
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
