using System.Text.Json;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Shared host/node logic for delivering scene attachment artifacts to remote render nodes.
///
/// The host (Render.BuildBlendFromRefs) materializes attachments next to the prepared blend and writes
/// a "&lt;blend&gt;.attachments.json" sidecar. Render.Split* reads that sidecar and carries the refs into
/// the per-batch / per-task payload (RenderTaskBatchData.Attachments / RenderTaskData.Attachments) so a
/// worker node — which only downloads the blend blob into an isolated cache dir — can materialize the
/// same dependency layout next to its working copy of the blend before rendering. Without this, external
/// dependencies (linked libraries, caches, volumes, …) never reach the node.
/// </summary>
internal static class RenderSceneAttachmentTransfer
{
    #region Constants

    public const string ManifestSuffix = ".attachments.json";

    private const string SceneAttachmentBlobPackaging = "SceneAttachmentBlob";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { PropertyNameCaseInsensitive = true };

    #endregion

    #region Functions

    /// <summary>
    /// Reads the attachment manifest sidecar written next to <paramref name="blendLocalPath"/> by
    /// Render.BuildBlendFromRefs. Returns an empty list when no sidecar exists (self-contained scene).
    /// </summary>
    public static IReadOnlyList<RenderSceneAttachmentRefData> TryLoadManifest(string blendLocalPath)
    {
        if (string.IsNullOrWhiteSpace(blendLocalPath))
            return [];

        var manifestPath = blendLocalPath + ManifestSuffix;
        if (!File.Exists(manifestPath))
            return [];

        var json = File.ReadAllText(manifestPath);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var attachments = JsonSerializer.Deserialize<List<RenderSceneAttachmentRefData>>(json, ManifestJsonOptions);
        return attachments ?? [];
    }

    /// <summary>
    /// Host-side, Split-friendly variant: resolves the scene blob's local path and reads its attachment
    /// sidecar. Returns an empty list when the blob cannot be resolved or has no sidecar — Render.Split*
    /// historically never touched the scene blob, so a fetch failure here must NOT become a new split-time
    /// failure (the render step still validates the blob). A self-contained scene simply has no sidecar.
    /// </summary>
    public static async Task<IReadOnlyList<RenderSceneAttachmentRefData>> TryLoadManifestForBlobAsync(
        IWitBlobService blobService,
        Guid sceneBlobId,
        ILogger logger)
    {
        try
        {
            var blendPath = await blobService.GetLocalPathAsync(sceneBlobId);
            return TryLoadManifest(blendPath);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "No attachment manifest resolved for scene blob {BlobId}; treating the scene as self-contained.", sceneBlobId);
            return [];
        }
    }

    /// <summary>
    /// Copies every SceneAttachmentBlob-packaged attachment from blob storage into
    /// <paramref name="sceneDirectory"/> at its RelativePath, recreating the addon-side layout so the
    /// blend's relative dependency references resolve. Non-blob-packaged entries are skipped.
    /// </summary>
    public static async Task MaterializeAsync(
        IWitBlobService blobService,
        string sceneDirectory,
        IReadOnlyList<RenderSceneAttachmentRefData> attachments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blobService);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneDirectory);
        ArgumentNullException.ThrowIfNull(attachments);

        foreach (var attachment in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(attachment.PackagingStrategy, SceneAttachmentBlobPackaging, StringComparison.Ordinal))
                continue;

            if (attachment.BlobId == Guid.Empty)
                throw new InvalidOperationException(
                    $"Scene attachment '{attachment.RelativePath}' uses SceneAttachmentBlob packaging but does not provide a BlobId.");

            var sourcePath = await blobService.GetLocalPathAsync(attachment.BlobId);
            var targetPath = ResolveAttachmentTargetPath(sceneDirectory, attachment.RelativePath);

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new InvalidOperationException($"Failed to resolve a target directory for attachment '{attachment.RelativePath}'.");

            Directory.CreateDirectory(targetDirectory);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    /// <summary>
    /// Resolves the materialization target for an attachment, guarding against path traversal outside
    /// the scene directory (mirrors the guard in Render.BuildBlendFromRefs).
    /// </summary>
    public static string ResolveAttachmentTargetPath(string sceneDirectory, string relativePath)
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
            throw new InvalidOperationException(
                $"Scene attachment RelativePath '{relativePath}' escapes the scene directory '{sceneDirectory}'.");

        return targetPath;
    }

    /// <summary>
    /// Node-side: copies the cached blend into a fresh working directory and materializes its
    /// attachments alongside it, so the blend's relative dependency references resolve locally for the
    /// render. The blob cache dir holds only the blend itself; rendering from there would miss every
    /// external dependency. Returns the working blend path and the working directory (delete it with
    /// <see cref="TryDeleteWorkingScene"/> once the render is done).
    /// </summary>
    public static async Task<(string BlendPath, string WorkingDirectory)> PrepareWorkingSceneAsync(
        IWitBlobService blobService,
        string tempRootPath,
        string sourceBlendPath,
        IReadOnlyList<RenderSceneAttachmentRefData> attachments,
        Guid jobId,
        int taskIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBlendPath);

        var workingDirectory = Path.Combine(tempRootPath, $"outwit_scene_{jobId:N}_{taskIndex}");
        Directory.CreateDirectory(workingDirectory);

        var workingBlendPath = Path.Combine(workingDirectory, Path.GetFileName(sourceBlendPath));
        File.Copy(sourceBlendPath, workingBlendPath, overwrite: true);

        await MaterializeAsync(blobService, workingDirectory, attachments, cancellationToken);

        return (workingBlendPath, workingDirectory);
    }

    /// <summary>Best-effort cleanup of a node working-scene directory created by <see cref="PrepareWorkingSceneAsync"/>.</summary>
    public static void TryDeleteWorkingScene(string? workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory))
            return;

        try
        {
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
        catch
        {
            // Best-effort — a render may still hold a file open; the node's temp sweep reclaims it later.
        }
    }

    #endregion
}
