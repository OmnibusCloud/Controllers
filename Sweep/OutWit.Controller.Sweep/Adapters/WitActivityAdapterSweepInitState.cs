using Microsoft.Extensions.Logging;
using OutWit.Controller.Sweep.Activities;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Adapters;

internal sealed class WitActivityAdapterSweepInitState : WitActivityAdapterFunction<WitActivitySweepInitState>
{
    #region Constructors

    public WitActivityAdapterSweepInitState(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override Task Process(WitActivitySweepInitState activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out SweepPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        SweepScheduleData? schedule = null;
        if (activity.Schedule != null)
            pool.TryGetValue(activity.Schedule, out schedule);

        // A cursor already in the pool before this first assignment is a resume: the host (or
        // a client, through the engine) handed the script the state of an earlier run of the
        // same study, and the schedule that run went by when it has it. The run continues
        // from the cursor under that schedule - the plan's when none was handed - once the
        // state is shown to fit; the chunks it harvested are never computed again, and a
        // state that does not fit is named and refused here, before any node sees a task.
        if (pool.TryGetValue(activity.ReturnReference, out SweepStateData? resumed) && resumed != null && SweepResume.IsStarted(resumed))
        {
            var findings = SweepResume.Findings(plan, resumed, schedule);
            if (findings.Count > 0)
                throw new InvalidOperationException($"The sweep cannot resume from the state it was handed: {string.Join("; ", findings)}.");

            if (schedule == null || schedule.ChunkSizes.Count == 0)
                schedule = SweepResume.ScheduleOf(plan);

            Logger.LogInformation(
                "Sweep resumes at chunk {Chunk} of {Chunks}: {Harvested} variant(s) harvested before, manifest {Manifest}",
                resumed.ChunkIndex, schedule.ChunkSizes.Count, resumed.NextVariantOrdinal, resumed.ManifestBlobId);
        }
        else
        {
            // A fresh run: the zero cursor, and the plan's schedule as the run's own. A
            // schedule handed without a started cursor means nothing and is replaced.
            resumed = new SweepStateData();
            schedule = SweepResume.ScheduleOf(plan);
        }

        // The schedule rides its own variable, written here once and never reassigned, so the
        // state keeps the wire layout every released reader speaks; a script that declares no
        // schedule variable (Sweep 2.0 - 2.1) runs by the plan's.
        if (activity.Schedule != null && !pool.TrySetValue(activity.Schedule.Reference, schedule))
            throw new InvalidOperationException($"Failed to set schedule '{activity.Schedule}'.");

        if (!pool.TrySetValue(activity.ReturnReference, resumed))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivitySweepInitState CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length is < 1 or > 2)
                throw new ArgumentException($"Expected 1 or 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            IWitReference? schedule = null;
            if (parameters.Length == 2 && (schedule = parameters[1] as IWitReference) == null)
                throw new ArgumentException("Parameter 'Schedule' must be a variable reference.");

            return new WitActivitySweepInitState
            {
                Plan = plan,
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
