using Microsoft.Extensions.Logging;
using OutWit.Math.Simulation;
using OutWit.Controller.Simulation.Parareal.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Math.Simulation.Model.Parareal;

namespace OutWit.Controller.Simulation.Parareal.Adapters;

internal sealed class WitActivityAdapterPararealIsConverged : WitActivityAdapterFunction<WitActivityPararealIsConverged>
{
    #region Constructors

    public WitActivityAdapterPararealIsConverged(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityPararealIsConverged activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.State, out PararealStateData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        var converged = state.Round > 0
                        && state.CorrectionNorm <= state.Eps * state.Scale;

        if (!pool.TrySetValue(activity.ReturnReference, converged))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        await Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivityPararealIsConverged CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference state)
                throw new ArgumentException("Parameter 'State' must be a variable reference.");

            return new WitActivityPararealIsConverged
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
