using Microsoft.Extensions.Logging;
using OutWit.Controller.Sweep.Activities;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Adapters;

internal sealed class WitActivityAdapterSweepChunkCount : WitActivityAdapterFunction<WitActivitySweepChunkCount>
{
    #region Constructors

    public WitActivityAdapterSweepChunkCount(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override Task Process(WitActivitySweepChunkCount activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out SweepPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        // The chunks left to run: with a state, those after the cursor's own by the run's
        // schedule - a resumed run loops over what it has not harvested yet; without one (a
        // Sweep 2.0 - 2.1 script), every chunk of the plan.
        var remaining = plan.ChunkSizes.Count;
        if (activity.State != null)
        {
            if (!pool.TryGetValue(activity.State, out SweepStateData? state) || state == null)
                throw new InvalidOperationException("Failed to get parameter 'State'.");

            SweepScheduleData? schedule = null;
            if (activity.Schedule != null)
                pool.TryGetValue(activity.Schedule, out schedule);

            remaining = Math.Max(0, SweepResume.Sizes(plan, schedule).Count - state.ChunkIndex);
        }

        if (!pool.TrySetValue(activity.ReturnReference, remaining))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivitySweepChunkCount CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length is < 1 or > 3)
                throw new ArgumentException($"Expected 1 to 3 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            IWitReference? state = null;
            if (parameters.Length >= 2 && (state = parameters[1] as IWitReference) == null)
                throw new ArgumentException("Parameter 'State' must be a variable reference.");

            IWitReference? schedule = null;
            if (parameters.Length == 3 && (schedule = parameters[2] as IWitReference) == null)
                throw new ArgumentException("Parameter 'Schedule' must be a variable reference.");

            return new WitActivitySweepChunkCount
            {
                Plan = plan,
                State = state,
                Schedule = schedule
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
