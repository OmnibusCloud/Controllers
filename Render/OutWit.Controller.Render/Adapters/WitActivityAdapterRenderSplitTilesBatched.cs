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
/// Adapter for <see cref="WitActivityRenderSplitTilesBatched"/>. Generates the same tile-task grid as
/// <c>Render.SplitTiles</c> (via <see cref="RenderTileTaskBuilder"/>) then groups the tiles into
/// <see cref="RenderTaskBatchData"/> chunks of the per-engine size (<see cref="RenderChunkPolicy"/>), so
/// Grid.ForEach distributes chunks that <c>Render.FrameBatch</c> renders one-Blender-process each.
/// </summary>
internal sealed class WitActivityAdapterRenderSplitTilesBatched :
    WitActivityAdapterFunction<WitActivityRenderSplitTilesBatched>
{
    #region Constructors

    public WitActivityAdapterRenderSplitTilesBatched(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Functions

    protected override WitActivityRenderSplitTilesBatched CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 6)
            throw new ArgumentException($"Render.SplitTilesBatched expects 6 parameters, got {parameters.Length}");

        return new WitActivityRenderSplitTilesBatched
        {
            Scene = parameters[0],
            Frame = parameters[1],
            TilesX = parameters[2],
            TilesY = parameters[3],
            Options = parameters[4],
            TileOptions = parameters[5]
        };
    }

    protected override async Task Process(
        WitActivityRenderSplitTilesBatched activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out Guid sceneId))
            throw new InvalidOperationException("Failed to get Blob parameter 'scene'");

        if (!pool.TryGetValue(activity.Frame, out int frame))
            throw new InvalidOperationException("Failed to get Int parameter 'frame'");

        if (!pool.TryGetValue(activity.TilesX, out int tilesX))
            throw new InvalidOperationException("Failed to get Int parameter 'tilesX'");

        if (!pool.TryGetValue(activity.TilesY, out int tilesY))
            throw new InvalidOperationException("Failed to get Int parameter 'tilesY'");

        if (!pool.TryGetValue(activity.Options, out RenderOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get RenderOptions parameter 'options'");

        if (!pool.TryGetValue(activity.TileOptions, out TileOptionsData? tileOptions) || tileOptions == null)
            throw new InvalidOperationException("Failed to get TileOptions parameter 'tileOptions'");

        if (tilesX <= 0)
            throw new InvalidOperationException($"tilesX must be > 0, got {tilesX}");

        if (tilesY <= 0)
            throw new InvalidOperationException($"tilesY must be > 0, got {tilesY}");

        RenderInputLimits.ValidateTileGrid(tilesX, tilesY);

        var (outputWidth, outputHeight) = await ResolveOutputResolutionAsync(sceneId, options, status.JobId);

        RenderTileTaskBuilder.ValidateTileOptions(tileOptions, outputWidth, outputHeight, tilesX, tilesY);

        var tasks = RenderTileTaskBuilder.BuildTileTasks(
            sceneId, frame, tilesX, tilesY, options, tileOptions, outputWidth, outputHeight);

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

        Logger.LogInformation(
            "Render.SplitTilesBatched: {Tiles} tiles (grid {TilesX}x{TilesY}, frame {Frame}) -> {Batches} chunks of up to {ChunkSize} ({Engine})",
            tasks.Count, tilesX, tilesY, frame, batches.Count, chunkSize, options.Engine);

        pool.TrySetValue(activity.ReturnReference, batches);
    }

    private async Task<(int Width, int Height)> ResolveOutputResolutionAsync(Guid sceneId, RenderOptionsData options, Guid jobId)
    {
        if (options.ResolutionX > 0 && options.ResolutionY > 0)
            return (options.ResolutionX, options.ResolutionY);

        var blendPath = await BlobService.GetLocalPathAsync(sceneId);
        var (sceneWidth, sceneHeight) = await GetBlenderRunner().GetSceneResolutionAsync(blendPath, ProcessingManager.CancellationToken(jobId));

        if (options.ResolutionX <= 0)
            options.ResolutionX = sceneWidth;

        if (options.ResolutionY <= 0)
            options.ResolutionY = sceneHeight;

        return (options.ResolutionX, options.ResolutionY);
    }

    private BlenderRunner GetBlenderRunner()
    {
        if (m_blenderRunner != null)
            return m_blenderRunner;

        var controllerAssemblyPath = typeof(WitControllerRenderModule).Assembly.Location;
        var blenderDir = RenderBinaryResolver.ResolveBlenderRoot(controllerAssemblyPath);
        m_blenderRunner = new BlenderRunner(blenderDir, Logger);
        if (!m_blenderRunner.IsAvailable)
            throw new InvalidOperationException($"Blender not found in controller module at '{blenderDir}'. Ensure the render controller module includes the Blender portable installation.");

        return m_blenderRunner;
    }

    #endregion

    #region Fields

    private BlenderRunner? m_blenderRunner;

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
