using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Dcc.Activities;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Services;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Adapters;

internal sealed class WitActivityAdapterRenderBuildBlendFromDccScene : WitActivityAdapterFunction<WitActivityRenderBuildBlendFromDccScene>
{
    #region Constructors

    public WitActivityAdapterRenderBuildBlendFromDccScene(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        IWitTempStorage tempStorage,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        TempStorage = tempStorage;
    }

    #endregion

    #region Functions

    protected override WitActivityRenderBuildBlendFromDccScene CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException($"Render.BuildBlendFromDccScene expects 1 parameter, got {parameters.Length}");

        return new WitActivityRenderBuildBlendFromDccScene
        {
            Scene = parameters[0]
        };
    }

    protected override async Task Process(
        WitActivityRenderBuildBlendFromDccScene activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out DccSceneData? scene) || scene == null)
            throw new InvalidOperationException("Failed to get DccScene parameter 'scene'");

        var buildInput = DccSceneBuildInputFactory.Create(scene);
        var buildArtifact = await DccBlendFileBuilder.BuildAsync(
            buildInput,
            BlobService,
            Logger,
            ProcessingManager.CancellationToken(status.JobId),
            TempStorage);

        Guid outputBlobId;
        try
        {
            outputBlobId = await BlobService.UploadFileAsync(buildArtifact.LocalBlendPath);
        }
        finally
        {
            DccBlendFileBuilder.Cleanup(buildArtifact, Logger);
        }

        if (!pool.TrySetValue(activity.ReturnReference, outputBlobId))
        {
            throw new InvalidOperationException(
                $"Failed to set return value '{activity.ReturnReference}' for Render.BuildBlendFromDccScene.");
        }
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    private IWitTempStorage TempStorage { get; }

    #endregion
}
