using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Dcc.Models.Build;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Services;

internal static class DccBlendFileBuilder
{
    #region Functions

    public static async Task<DccBlendBuildArtifact> BuildAsync(
        DccSceneBuildInput buildInput,
        IWitBlobService blobService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var workDirectory = Path.Combine(Path.GetTempPath(), $"outwit_render_dcc_build_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);

        await MaterializeAttachmentsAsync(workDirectory, buildInput, blobService);

        var outputBlendPath = Path.Combine(workDirectory, SanitizeFileName(buildInput.Scene.SceneName) + ".blend");
        var scriptBuildInput = CreateScriptBuildInput(buildInput, workDirectory);
        var pythonScript = DccBlenderSceneScriptGenerator.Create(scriptBuildInput);
        var pythonScriptPath = Path.Combine(workDirectory, "build_scene.py");
        await File.WriteAllTextAsync(
            pythonScriptPath,
            pythonScript
            + Environment.NewLine
            + "bpy.ops.file.pack_all()"
            + Environment.NewLine
            + $"bpy.ops.wm.save_as_mainfile(filepath={ToPythonStringLiteral(outputBlendPath)})"
            + Environment.NewLine,
            cancellationToken);

        var blenderPath = DccBlenderBinaryResolver.ResolveBlenderPath(Assembly.GetExecutingAssembly().Location, logger);
        if (!File.Exists(blenderPath))
            throw new FileNotFoundException($"Blender executable was not found at '{blenderPath}'.", blenderPath);

        await RunBlenderAsync(blenderPath, pythonScriptPath, workDirectory, logger, cancellationToken);

        if (!File.Exists(outputBlendPath))
            throw new InvalidOperationException($"Render.BuildBlendFromDccScene expected Blender to create '{outputBlendPath}', but the file was not found.");

        return new DccBlendBuildArtifact
        {
            LocalBlendPath = outputBlendPath,
            WorkDirectory = workDirectory
        };
    }

    public static void Cleanup(DccBlendBuildArtifact artifact, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(artifact.WorkDirectory))
            return;

        TryDeleteDirectory(artifact.WorkDirectory, logger);
    }

    private static async Task MaterializeAttachmentsAsync(
        string workDirectory,
        DccSceneBuildInput buildInput,
        IWitBlobService blobService)
    {
        foreach (var attachment in buildInput.Scene.AttachedFiles)
        {
            if (!string.Equals(attachment.PackagingStrategy, "SceneAttachmentBlob", StringComparison.Ordinal))
                continue;

            if (attachment.BlobId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Render.BuildBlendFromDccScene attachment '{attachment.RelativePath}' requires a BlobId when PackagingStrategy is SceneAttachmentBlob.");
            }

            var sourcePath = await blobService.GetLocalPathAsync(attachment.BlobId);
            var targetPath = ResolveAttachmentTargetPath(workDirectory, attachment.RelativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath)
                                  ?? throw new InvalidOperationException($"Failed to resolve target directory for attachment '{attachment.RelativePath}'.");
            Directory.CreateDirectory(targetDirectory);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static DccSceneBuildInput CreateScriptBuildInput(DccSceneBuildInput buildInput, string workDirectory)
    {
        var absoluteImageAttachments = buildInput.ImageAttachmentsByImageId.ToDictionary(
            me => me.Key,
            me => new RenderSceneAttachmentRefData
            {
                Kind = me.Value.Kind,
                BlobId = me.Value.BlobId,
                OriginalPath = me.Value.OriginalPath,
                RelativePath = Path.IsPathRooted(me.Value.RelativePath)
                    ? me.Value.RelativePath
                    : Path.Combine(workDirectory, me.Value.RelativePath),
                PackagingStrategy = me.Value.PackagingStrategy
            },
            StringComparer.Ordinal);

        return new DccSceneBuildInput
        {
            Scene = buildInput.Scene,
            UnitsToMetersScale = buildInput.UnitsToMetersScale,
            NodesById = buildInput.NodesById,
            MeshesById = buildInput.MeshesById,
            MaterialsById = buildInput.MaterialsById,
            ImageAssetsById = buildInput.ImageAssetsById,
            ImageAttachmentsByImageId = absoluteImageAttachments
        };
    }

    private static async Task RunBlenderAsync(
        string blenderPath,
        string pythonScriptPath,
        string workDirectory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = blenderPath,
            Arguments = $"-b --factory-startup --python-exit-code 1 --python \"{pythonScriptPath}\"",
            WorkingDirectory = workDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            TryKill(process, logger);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Render.BuildBlendFromDccScene failed to generate a .blend file. ExitCode={process.ExitCode}. StdOut={stdout}. StdErr={stderr}");
        }
    }

    private static string ResolveAttachmentTargetPath(string workDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("Render.BuildBlendFromDccScene attachment RelativePath is required.");

        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException($"Render.BuildBlendFromDccScene attachment RelativePath must be relative, got '{relativePath}'.");

        var baseDirectory = Path.GetFullPath(workDirectory);
        var targetPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
        var expectedPrefix = baseDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? baseDirectory
            : baseDirectory + Path.DirectorySeparatorChar;

        if (!targetPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Render.BuildBlendFromDccScene attachment RelativePath '{relativePath}' escapes the working directory '{workDirectory}'.");
        }

        return targetPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(me => invalidCharacters.Contains(me) ? '_' : me).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "scene" : sanitized;
    }

    private static string ToPythonStringLiteral(string value)
    {
        return $"'{value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", string.Empty).Replace("\n", "\\n")}'";
    }

    private static void TryDeleteDirectory(string workDirectory, ILogger logger)
    {
        try
        {
            if (Directory.Exists(workDirectory))
                Directory.Delete(workDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temporary DCC build directory '{WorkDirectory}'.", workDirectory);
        }
    }

    private static void TryKill(Process process, ILogger logger)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to terminate Blender process '{ProcessId}' during DCC build cleanup.", process.Id);
        }
    }

    #endregion
}
