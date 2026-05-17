using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderBuildBlend : WitActivityAdapterFunction<WitActivityRenderBuildBlend>
{
    #region Constructors

    public WitActivityAdapterRenderBuildBlend(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Functions

    protected override WitActivityRenderBuildBlend CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException($"Render.BuildBlend expects 1 parameter, got {parameters.Length}");

        return new WitActivityRenderBuildBlend
        {
            Scene = parameters[0]
        };
    }

    protected override async Task Process(
        WitActivityRenderBuildBlend activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out RenderSceneData? scene) || scene == null)
            throw new InvalidOperationException("Failed to get RenderScene parameter 'scene'");

        ValidateScene(scene);

        var fileName = string.IsNullOrWhiteSpace(scene.FileName) ? "scene.blend" : scene.FileName;
        var blobId = await BlobService.UploadBytesAsync(scene.BlendFileBytes, fileName);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.BuildBlend.");
    }

    private static void ValidateScene(RenderSceneData scene)
    {
        if (scene.BlendFileBytes.Length == 0)
            throw new InvalidOperationException("Render.BuildBlend requires a non-empty inline .blend payload.");
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
