using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Variables;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

/// <summary>
/// Adapter for <see cref="WitActivityRenderCollect"/>.
/// Collects render results and assembles the final output.
/// For frame-based rendering: sorts results by index, returns ordered BlobCollection.
/// For tile-based (Phase 5): will perform image stitching + optional denoise.
/// Host-side only. No Blender required for frame collection.
/// </summary>
internal sealed class WitActivityAdapterRenderCollect :
    WitActivityAdapterFunction<WitActivityRenderCollect>
{
    #region Constructors

    public WitActivityAdapterRenderCollect(
        IWitProcessingManager processingManager,
        ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Functions

    protected override WitActivityRenderCollect CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 2)
            throw new ArgumentException($"Render.Collect expects 2 parameters, got {parameters.Length}");

        return new WitActivityRenderCollect
        {
            Results = parameters[0],
            Options = parameters[1]
        };
    }

    protected override Task Process(
        WitActivityRenderCollect activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetCollection(activity.Results, out IReadOnlyList<RenderResultData?>? results) || results == null)
            throw new InvalidOperationException("Failed to get RenderResultCollection parameter 'results'");

        if (!pool.TryGetValue(activity.Options, out RenderOptionsData? options))
            throw new InvalidOperationException("Failed to get RenderOptions parameter 'options'");

        // Sort results by index to ensure correct frame ordering
        var sorted = results
            .Where(r => r != null)
            .OrderBy(r => r!.Index)
            .ToList();

        if (sorted.Count == 0)
            throw new InvalidOperationException("No render results to collect");

        // For frame-based: return sorted list of image blob IDs
        var blobIds = sorted.Select(r => (Guid?)r!.ImageBlobId).ToList();

        Logger.LogInformation("Render.Collect: assembled {Count} results, frames {First}-{Last}",
            sorted.Count, sorted.First()!.Index, sorted.Last()!.Index);

        pool.TrySetValue(activity.ReturnReference, blobIds);

        return Task.CompletedTask;
    }

    #endregion
}
