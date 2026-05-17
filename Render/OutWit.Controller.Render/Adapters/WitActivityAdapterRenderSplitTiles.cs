using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderSplitTiles : WitActivityAdapterFunction<WitActivityRenderSplitTiles>
{
    #region Constructors

    public WitActivityAdapterRenderSplitTiles(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Functions

    protected override WitActivityRenderSplitTiles CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 6)
            throw new ArgumentException($"Render.SplitTiles expects 6 parameters, got {parameters.Length}");

        return new WitActivityRenderSplitTiles
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
        WitActivityRenderSplitTiles activity,
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

        var (outputWidth, outputHeight) = await ResolveOutputResolutionAsync(sceneId, options, status.JobId);

        ValidateTileOptions(tileOptions, outputWidth, outputHeight, tilesX, tilesY);

        var tasks = new List<RenderTaskData>(tilesX * tilesY);
        var taskIndex = 0;

        for (var y = 0; y < tilesY; y++)
        {
            for (var x = 0; x < tilesX; x++)
            {
                tasks.Add(new RenderTaskData
                {
                    SceneBlobId = sceneId,
                    Frame = frame,
                    TileMinX = x / (float)tilesX,
                    TileMaxX = (x + 1) / (float)tilesX,
                    TileMinY = y / (float)tilesY,
                    TileMaxY = (y + 1) / (float)tilesY,
                    RenderMinX = CalculateRenderMinX(x, tilesX, tileOptions, outputWidth),
                    RenderMaxX = CalculateRenderMaxX(x, tilesX, tileOptions, outputWidth),
                    RenderMinY = CalculateRenderMinY(y, tilesY, tileOptions, outputHeight),
                    RenderMaxY = CalculateRenderMaxY(y, tilesY, tileOptions, outputHeight),
                    TaskIndex = taskIndex++,
                    Options = (RenderOptionsData)options.Clone()
                });
            }
        }

        Logger.LogInformation("Render.SplitTiles: generated {Count} tile tasks for frame {Frame} using grid {TilesX}x{TilesY}",
            tasks.Count, frame, tilesX, tilesY);

        pool.TrySetValue(activity.ReturnReference, tasks);

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

    private static float CalculateRenderMinX(int tileX, int tilesX, TileOptionsData tileOptions, int outputWidth)
    {
        return Math.Max(0f, tileX / (float)tilesX - tileOptions.OverlapPx / (float)outputWidth);
    }

    private static float CalculateRenderMaxX(int tileX, int tilesX, TileOptionsData tileOptions, int outputWidth)
    {
        return Math.Min(1f, (tileX + 1) / (float)tilesX + tileOptions.OverlapPx / (float)outputWidth);
    }

    private static float CalculateRenderMinY(int tileY, int tilesY, TileOptionsData tileOptions, int outputHeight)
    {
        return Math.Max(0f, tileY / (float)tilesY - tileOptions.OverlapPx / (float)outputHeight);
    }

    private static float CalculateRenderMaxY(int tileY, int tilesY, TileOptionsData tileOptions, int outputHeight)
    {
        return Math.Min(1f, (tileY + 1) / (float)tilesY + tileOptions.OverlapPx / (float)outputHeight);
    }

    private static void ValidateTileOptions(TileOptionsData tileOptions, int outputWidth, int outputHeight, int tilesX, int tilesY)
    {
        if (tileOptions.OverlapPx < 0)
            throw new InvalidOperationException($"TileOptions.OverlapPx must be >= 0, got {tileOptions.OverlapPx}.");

        var coreTileWidth = Math.Max(1, outputWidth / tilesX);
        var coreTileHeight = Math.Max(1, outputHeight / tilesY);
        if (tileOptions.OverlapPx >= coreTileWidth || tileOptions.OverlapPx >= coreTileHeight)
        {
            throw new InvalidOperationException(
                $"TileOptions.OverlapPx must be smaller than the core tile size. Got {tileOptions.OverlapPx}px for tile size {coreTileWidth}x{coreTileHeight}.");
        }
    }

    #endregion

    #region Fields

    private BlenderRunner? m_blenderRunner;

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
