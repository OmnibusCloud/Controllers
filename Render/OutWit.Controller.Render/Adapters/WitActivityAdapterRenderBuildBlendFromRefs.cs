using Microsoft.Extensions.Logging;
using System.Text.Json;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderBuildBlendFromRefs : WitActivityAdapterFunction<WitActivityRenderBuildBlendFromRefs>
{
    #region Constructors

    public WitActivityAdapterRenderBuildBlendFromRefs(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Functions

    protected override WitActivityRenderBuildBlendFromRefs CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException($"Render.BuildBlendFromRefs expects 1 parameter, got {parameters.Length}");

        return new WitActivityRenderBuildBlendFromRefs
        {
            Scene = parameters[0]
        };
    }

    protected override async Task Process(
        WitActivityRenderBuildBlendFromRefs activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out RenderSceneRefData? scene) || scene == null)
            throw new InvalidOperationException("Failed to get RenderSceneRef parameter 'scene'");

        if (scene.BlendBlobId == Guid.Empty)
            throw new InvalidOperationException("Render.BuildBlendFromRefs requires a non-empty BlendBlobId.");

        var localPath = await BlobService.GetLocalPathAsync(scene.BlendBlobId);
        var outputBlobId = scene.BlendBlobId;
        var outputPath = localPath;

        if (scene.AttachedFiles.Count > 0)
        {
            outputBlobId = await BlobService.UploadFileAsync(localPath);
            outputPath = await BlobService.GetLocalPathAsync(outputBlobId);
            await MaterializeAttachmentsAsync(outputPath, scene);

            if (RequiresAttachmentPathRemap(scene))
            {
                var blenderRunner = RenderBenchmarkHelper.TryCreateBlenderRunner(Logger)
                                    ?? throw new InvalidOperationException("Blender is required to rewrite attachment-backed dependency paths in the working scene copy.");
                await BlenderSceneAttachmentRemapHelper.RemapAttachmentPathsInPlaceAsync(
                    blenderRunner,
                    outputPath,
                    scene.AttachedFiles,
                    ProcessingManager.CancellationToken(status.JobId));
            }

            await PersistAttachmentManifestAsync(outputPath, scene);
        }

        if (!pool.TrySetValue(activity.ReturnReference, outputBlobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.BuildBlendFromRefs.");
    }

    private static async Task PersistAttachmentManifestAsync(string localPath, RenderSceneRefData scene)
    {
        if (string.IsNullOrWhiteSpace(localPath) || scene.AttachedFiles.Count == 0)
            return;

        var manifestPath = localPath + ".attachments.json";
        var json = JsonSerializer.Serialize(scene.AttachedFiles);
        await File.WriteAllTextAsync(manifestPath, json);
    }

    private static bool RequiresAttachmentPathRemap(RenderSceneRefData scene)
    {
        return scene.AttachedFiles.Any(me => BlenderSceneAttachmentRemapHelper.IsRemappedAttachmentKind(me.Kind)
                                             && string.Equals(me.PackagingStrategy, "SceneAttachmentBlob", StringComparison.Ordinal));
    }

    private async Task MaterializeAttachmentsAsync(string localPath, RenderSceneRefData scene)
    {
        if (string.IsNullOrWhiteSpace(localPath) || scene.AttachedFiles.Count == 0)
            return;

        var sceneDirectory = Path.GetDirectoryName(localPath);
        if (string.IsNullOrWhiteSpace(sceneDirectory))
            throw new InvalidOperationException($"Failed to resolve the scene directory for '{localPath}'.");

        foreach (var attachment in scene.AttachedFiles)
        {
            if (!string.Equals(attachment.PackagingStrategy, "SceneAttachmentBlob", StringComparison.Ordinal))
                continue;

            if (attachment.BlobId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Scene attachment '{attachment.RelativePath}' uses SceneAttachmentBlob packaging but does not provide a BlobId.");
            }

            var sourcePath = await BlobService.GetLocalPathAsync(attachment.BlobId);
            var targetPath = ResolveAttachmentTargetPath(sceneDirectory, attachment.RelativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new InvalidOperationException($"Failed to resolve a target directory for attachment '{attachment.RelativePath}'.");

            Directory.CreateDirectory(targetDirectory);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static string ResolveAttachmentTargetPath(string sceneDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("Scene attachment RelativePath is required.");

        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException($"Scene attachment RelativePath must be relative, got '{relativePath}'.");

        var baseDirectory = Path.GetFullPath(sceneDirectory);
        var targetPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
        var expectedPrefix = baseDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? baseDirectory
            : baseDirectory + Path.DirectorySeparatorChar;

        if (!targetPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Scene attachment RelativePath '{relativePath}' escapes the scene directory '{sceneDirectory}'.");
        }

        return targetPath;
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
