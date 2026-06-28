using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

/// <summary>
/// Adapter for <see cref="WitActivityRenderSplitBatched"/>. Generates the per-frame tasks (as
/// Render.Split does) then groups them into <see cref="RenderTaskBatchData"/> chunks of the per-engine
/// size (see <see cref="RenderChunkPolicy"/>), so Grid.ForEach distributes chunks that Render.FrameBatch
/// renders one-Blender-process each. Host-side only; no Blender.
/// </summary>
internal sealed class WitActivityAdapterRenderSplitBatched :
    WitActivityAdapterFunction<WitActivityRenderSplitBatched>
{
    #region Constructors

    public WitActivityAdapterRenderSplitBatched(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Functions

    protected override WitActivityRenderSplitBatched CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 4)
            throw new ArgumentException($"Render.SplitBatched expects 4 parameters, got {parameters.Length}");

        return new WitActivityRenderSplitBatched
        {
            Scene = parameters[0],
            StartFrame = parameters[1],
            EndFrame = parameters[2],
            Options = parameters[3]
        };
    }

    protected override async Task Process(
        WitActivityRenderSplitBatched activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out Guid sceneId))
            throw new InvalidOperationException("Failed to get Blob parameter 'scene'");

        if (!pool.TryGetValue(activity.StartFrame, out int startFrame))
            throw new InvalidOperationException("Failed to get Int parameter 'startFrame'");

        if (!pool.TryGetValue(activity.EndFrame, out int endFrame))
            throw new InvalidOperationException("Failed to get Int parameter 'endFrame'");

        if (!pool.TryGetValue(activity.Options, out RenderOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get RenderOptions parameter 'options'");

        if (endFrame < startFrame)
            throw new InvalidOperationException($"endFrame ({endFrame}) must be >= startFrame ({startFrame})");

        var tasks = new List<RenderTaskData>();
        int taskIndex = 0;
        for (int frame = startFrame; frame <= endFrame; frame++)
        {
            tasks.Add(new RenderTaskData
            {
                SceneBlobId = sceneId,
                Frame = frame,
                TileMinX = 0f,
                TileMaxX = 1f,
                TileMinY = 0f,
                TileMaxY = 1f,
                TaskIndex = taskIndex++,
                Options = (RenderOptionsData)options.Clone()
            });
        }

        var chunkSize = RenderChunkPolicy.ComputeChunkSize(options.Engine, tasks.Count, options.BatchSize);

        var batches = new List<RenderTaskBatchData>();
        for (int i = 0; i < tasks.Count; i += chunkSize)
        {
            var chunk = tasks.GetRange(i, Math.Min(chunkSize, tasks.Count - i));
            batches.Add(new RenderTaskBatchData
            {
                SceneBlobId = sceneId,
                Options = (RenderOptionsData)options.Clone(),
                Tasks = chunk
            });
        }

        var attachments = await RenderSceneAttachmentTransfer.TryLoadManifestForBlobAsync(BlobService, sceneId, Logger);
        if (attachments.Count > 0)
        {
            foreach (var batch in batches)
                batch.Attachments = attachments.Select(me => (RenderSceneAttachmentRefData)me.Clone()).ToList();
        }

        Logger.LogInformation(
            "Render.SplitBatched: {Frames} frames ({Start}-{End}) -> {Batches} chunks of up to {ChunkSize} ({Engine}); {Attachments} scene attachment(s) per chunk",
            tasks.Count, startFrame, endFrame, batches.Count, chunkSize, options.Engine, attachments.Count);

        pool.TrySetValue(activity.ReturnReference, batches);
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
