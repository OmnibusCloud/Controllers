using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderCollectStill : WitActivityAdapterFunction<WitActivityRenderCollectStill>
{
    #region Constructors

    public WitActivityAdapterRenderCollectStill(
        IWitProcessingManager processingManager,
        ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Functions

    protected override WitActivityRenderCollectStill CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException($"Render.CollectStill expects 2 parameters, got {parameters.Length}");

        return new WitActivityRenderCollectStill
        {
            Results = parameters[0],
            Options = parameters[1]
        };
    }

    protected override Task Process(
        WitActivityRenderCollectStill activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetCollection(activity.Results, out IReadOnlyList<RenderResultData?>? results) || results == null)
            throw new InvalidOperationException("Failed to get RenderResultCollection parameter 'results'");

        if (!pool.TryGetValue(activity.Options, out RenderOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get RenderOptions parameter 'options'");

        var sorted = results
            .Where(me => me != null)
            .OrderBy(me => me!.Index)
            .Select(me => me!)
            .ToList();

        if (sorted.Count == 0)
            throw new InvalidOperationException("No render results to collect for Render.CollectStill.");

        if (sorted.Count != 1)
        {
            throw new InvalidOperationException(
                $"Render.CollectStill requires exactly one render result, got {sorted.Count}.");
        }

        Logger.LogInformation("Render.CollectStill: collected still result for frame index {Index}", sorted[0].Index);

        if (!pool.TrySetValue(activity.ReturnReference, sorted[0].ImageBlobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.CollectStill.");

        return Task.CompletedTask;
    }

    #endregion
}
