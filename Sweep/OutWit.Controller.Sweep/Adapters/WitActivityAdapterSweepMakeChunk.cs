using System.Collections;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Sweep.Activities;
using OutWit.Controller.Sweep.Families;
using OutWit.Controller.Sweep.Model;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Adapters;

internal sealed class WitActivityAdapterSweepMakeChunk : WitActivityAdapterFunction<WitActivitySweepMakeChunk>
{
    #region Constructors

    public WitActivityAdapterSweepMakeChunk(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySweepMakeChunk activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out SweepPlanData? plan) || plan?.Options == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetValue(activity.State, out SweepStateData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        if (state.ChunkIndex >= plan.ChunkSizes.Count)
            throw new InvalidOperationException(
                $"Chunk {state.ChunkIndex} requested, but the plan holds {plan.ChunkSizes.Count} chunk(s).");

        var family = SweepFamilies.Of(plan);
        var variants = plan.Options.Variants
            .Skip(state.NextVariantOrdinal)
            .Take(plan.ChunkSizes[state.ChunkIndex])
            .ToList();

        var tasks = await family.MakeTasksAsync(plan.Options, variants, BlobService);

        if (!pool.TrySetCollection(activity.ReturnReference, tasks))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        // A script whose task collection holds another family's tasks gets an
        // EMPTY collection from the engine, silently - the grid would then fan
        // out nothing and the harvest fail far from the cause. Read it back.
        if (!pool.TryGetObject(activity.ReturnReference, out var stored)
            || stored is not IEnumerable storedTasks
            || storedTasks.Cast<object?>().Count(task => task != null && family.TaskType.IsInstanceOfType(task)) != tasks.Count)
        {
            throw new InvalidOperationException(
                $"The script's task collection does not hold {family.TaskType.Name}: {family.Family} studies run with the {family.ScriptName} script.");
        }
    }

    #endregion

    #region Parsing

    protected override WitActivitySweepMakeChunk CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            if (parameters[1] is not IWitReference state)
                throw new ArgumentException("Parameter 'State' must be a variable reference.");

            return new WitActivitySweepMakeChunk
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

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
