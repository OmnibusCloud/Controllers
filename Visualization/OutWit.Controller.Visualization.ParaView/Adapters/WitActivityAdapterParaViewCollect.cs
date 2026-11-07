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
/// Adapter for <see cref="WitActivityParaViewCollect"/>: flattens per-frame or batch results, orders
/// them by task index, fails on missing, duplicate or conflicting identities, and returns the image
/// blobs as the frame set.
/// </summary>
internal sealed class WitActivityAdapterParaViewCollect : WitActivityAdapterFunction<WitActivityParaViewCollect>
{
    #region Constructors

    public WitActivityAdapterParaViewCollect(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Functions

    protected override WitActivityParaViewCollect CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException($"ParaView.Collect expects 2 parameters (ParaViewRenderResultCollection, ParaViewOutputOptions), got {parameters.Length}");

        return new WitActivityParaViewCollect
        {
            Results = parameters[0],
            Options = parameters[1]
        };
    }

    protected override Task Process(
        WitActivityParaViewCollect activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        // Either shape: per-frame results (ParaView.RenderFrame) or batch results (ParaView.RenderFrameBatch).
        if (!ParaViewResultFlattener.TryFlatten(pool, activity.Results, out var results) || results == null)
            throw new InvalidOperationException("Failed to get ParaViewRenderResultCollection or ParaViewRenderResultBatchCollection parameter 'results'");

        if (!pool.TryGetValue(activity.Options, out ParaViewOutputOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get ParaViewOutputOptions parameter 'options'");

        var ordered = ParaViewResultOrdering.Order(results, "ParaView.Collect");
        var blobIds = ordered.Select(me => (Guid?)me.ImageBlobId).ToList();

        Logger.LogInformation("ParaView.Collect: assembled {Count} frame(s) of {Width}x{Height} {Format}, timesteps {First}..{Last}",
            ordered.Count, options.Width, options.Height, options.Format, ordered[0].TimestepIndex, ordered[^1].TimestepIndex);

        if (!pool.TrySetValue(activity.ReturnReference, blobIds))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for ParaView.Collect.");

        return Task.CompletedTask;
    }

    #endregion
}
