using Microsoft.Extensions.Logging;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Parareal.Activities;
using OutWit.Controller.Simulation.Parareal.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Adapters;

internal sealed class WitActivityAdapterPararealMakeTasks : WitActivityAdapterFunction<WitActivityPararealMakeTasks>
{
    #region Constructors

    public WitActivityAdapterPararealMakeTasks(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityPararealMakeTasks activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out PararealPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetValue(activity.State, out PararealStateData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        var tasks = PararealTaskFactory.BuildTasks(plan, state, emitSnapshots: false);

        if (!pool.TrySetCollection(activity.ReturnReference, tasks))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        await Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivityPararealMakeTasks CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            if (parameters[1] is not IWitReference state)
                throw new ArgumentException("Parameter 'State' must be a variable reference.");

            return new WitActivityPararealMakeTasks
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
