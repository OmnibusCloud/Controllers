using Microsoft.Extensions.Logging;
using OutWit.Math.Simulation;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Controller.Simulation.Schwarz.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Math.Simulation.Model.Schwarz;

namespace OutWit.Controller.Simulation.Schwarz.Adapters;

internal sealed class WitActivityAdapterSchwarzMakeTasks : WitActivityAdapterFunction<WitActivitySchwarzMakeTasks>
{
    #region Constructors

    public WitActivityAdapterSchwarzMakeTasks(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySchwarzMakeTasks activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out SchwarzPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetValue(activity.State, out SchwarzRoundData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        var tasks = SchwarzTaskFactory.BuildTasks(plan, state, emitField: false);

        if (!pool.TrySetCollection(activity.ReturnReference, tasks))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        await Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivitySchwarzMakeTasks CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            if (parameters[1] is not IWitReference state)
                throw new ArgumentException("Parameter 'State' must be a variable reference.");

            return new WitActivitySchwarzMakeTasks
            {
                Plan = plan,
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
