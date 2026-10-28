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

        // A cursor already in the pool before this first assignment is a resume: the host (or
        // a client, through the engine) handed the script the state of an earlier run of the
        // same study. The run continues from it - the chunks it harvested are never computed
        // again - once the state is shown to fit the plan; a state that does not fit is named
        // and refused here, before any node sees a task.
        if (pool.TryGetValue(activity.ReturnReference, out SweepStateData? resumed) && resumed != null && SweepResume.IsStarted(resumed))
        {
            var findings = SweepResume.Findings(plan, resumed);
            if (findings.Count > 0)
                throw new InvalidOperationException($"The sweep cannot resume from the state it was handed: {string.Join("; ", findings)}.");

            // A cursor an older host wrote carries no schedule of its own: it ran by the
            // plan's, which the fit check above held it to.
            if (resumed.ChunkSizes.Count == 0)
                resumed.ChunkSizes = [.. plan.ChunkSizes];

            Logger.LogInformation(
                "Sweep resumes at chunk {Chunk} of {Chunks}: {Harvested} variant(s) harvested before, manifest {Manifest}",
                resumed.ChunkIndex, resumed.ChunkSizes.Count, resumed.NextVariantOrdinal, resumed.ManifestBlobId);

            if (!pool.TrySetValue(activity.ReturnReference, resumed))
                throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

            return Task.CompletedTask;
        }

        // The zero cursor carries the schedule this run goes by, so a client mirrors the
        // server's plan from the state alone and a later resume keeps these boundaries.
        var state = new SweepStateData
        {
            ChunkSizes = [.. plan.ChunkSizes]
        };

        if (!pool.TrySetValue(activity.ReturnReference, state))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        return Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivitySweepInitState CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            return new WitActivitySweepInitState
            {
                Plan = plan
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
