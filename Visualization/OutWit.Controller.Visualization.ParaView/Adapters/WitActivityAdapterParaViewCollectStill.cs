using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Activities;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Adapters;

/// <summary>
/// Adapter for <see cref="WitActivityParaViewCollectStill"/>: requires exactly one result and returns its image blob.
/// </summary>
internal sealed class WitActivityAdapterParaViewCollectStill : WitActivityAdapterFunction<WitActivityParaViewCollectStill>
{
    #region Constructors

    public WitActivityAdapterParaViewCollectStill(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Functions

    protected override WitActivityParaViewCollectStill CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException($"ParaView.CollectStill expects 2 parameters (ParaViewRenderResultCollection, ParaViewOutputOptions), got {parameters.Length}");

        return new WitActivityParaViewCollectStill
        {
            Results = parameters[0],
            Options = parameters[1]
        };
    }

    protected override Task Process(
        WitActivityParaViewCollectStill activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetCollection<ParaViewRenderResultData>(activity.Results, out var results) || results == null)
            throw new InvalidOperationException("Failed to get ParaViewRenderResultCollection parameter 'results'");

        if (!pool.TryGetValue(activity.Options, out ParaViewOutputOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get ParaViewOutputOptions parameter 'options'");

        var ordered = ParaViewResultOrdering.Order(results, "ParaView.CollectStill");
        if (ordered.Count != 1)
            throw new InvalidOperationException($"ParaView.CollectStill requires exactly one render result, got {ordered.Count}.");

        Logger.LogInformation("ParaView.CollectStill: collected timestep {Timestep} ({Width}x{Height} {Format})",
            ordered[0].TimestepIndex, ordered[0].Width, ordered[0].Height, ordered[0].Format);

        if (!pool.TrySetValue(activity.ReturnReference, ordered[0].ImageBlobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for ParaView.CollectStill.");

        return Task.CompletedTask;
    }

    #endregion
}
