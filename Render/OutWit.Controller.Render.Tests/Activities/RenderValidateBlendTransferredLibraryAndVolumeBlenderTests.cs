using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class RenderValidateBlendTransferredLibraryAndVolumeBlenderTests
{
    #region Fields

    private BlenderRunner m_blenderRunner = null!;

    private string m_tempDirectory = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(solutionRoot);
        if (blenderDir == null)
            Assert.Ignore("No supported Blender prerequisites for current OS/architecture");

        m_blenderRunner = new BlenderRunner(blenderDir, NullLogger.Instance);
        if (!m_blenderRunner.IsAvailable)
            Assert.Ignore($"Blender not found at {blenderDir}");
    }

    [SetUp]
    public void SetUp()
    {
        m_tempDirectory = Path.Combine(Path.GetTempPath(), $"witcloud_validate_library_volume_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_tempDirectory))
            Directory.Delete(m_tempDirectory, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task ValidateBlendDetailedAsyncDoesNotReportLinkedLibraryWarningAfterAttachmentRemapTest()
    {
        var libraryBlendPath = Path.Combine(m_tempDirectory, "library.blend");
        await CreateBlendFileAsync(
            libraryBlendPath,
            [
                "mesh = bpy.data.meshes.new('LibraryMesh')",
                "obj = bpy.data.objects.new('LibraryCube', mesh)",
                "bpy.context.scene.collection.objects.link(obj)"
            ]);

        var linkedBlendPath = Path.Combine(m_tempDirectory, "scene_with_transferred_library.blend");
        await CreateBlendFileAsync(
            linkedBlendPath,
            [
                $"library_path = r'{NormalizePythonPath(libraryBlendPath)}'",
                "with bpy.data.libraries.load(library_path, link=True) as (data_from, data_to):",
                "    data_to.objects = ['LibraryCube']",
                "for obj in data_to.objects:",
                "    if obj is not None:",
                "        bpy.context.scene.collection.objects.link(obj)"
            ]);

        var attachment = new RenderSceneAttachmentRefData
        {
            Kind = "LinkedLibrary",
            OriginalPath = libraryBlendPath,
            RelativePath = "deps/linked-libraries/library.blend",
            PackagingStrategy = "SceneAttachmentBlob"
        };

        var materializedLibraryPath = Path.Combine(m_tempDirectory, "deps", "linked-libraries", "library.blend");
        Directory.CreateDirectory(Path.GetDirectoryName(materializedLibraryPath)!);
        File.Copy(libraryBlendPath, materializedLibraryPath, overwrite: true);

        await RemapAttachmentPathsInPlaceAsync(linkedBlendPath, [attachment]);
        await File.WriteAllTextAsync(linkedBlendPath + ".attachments.json", JsonSerializer.Serialize(new[] { attachment }));
        File.Delete(libraryBlendPath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(linkedBlendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncDoesNotReportVolumeWarningAfterAttachmentRemapTest()
    {
        var originalVolumePath = Path.Combine(m_tempDirectory, "external_volume.vdb");
        CreateDummyFile(originalVolumePath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_transferred_volume.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "volume = bpy.data.volumes.new('TransferredVolume')",
                $"volume.filepath = r'{NormalizePythonPath(originalVolumePath)}'",
                "volume.use_fake_user = True"
            ]);

        var attachment = new RenderSceneAttachmentRefData
        {
            Kind = "Volume",
            OriginalPath = originalVolumePath,
            RelativePath = "deps/volumes/transferred_volume.vdb",
            PackagingStrategy = "SceneAttachmentBlob"
        };

        var materializedVolumePath = Path.Combine(m_tempDirectory, "deps", "volumes", "transferred_volume.vdb");
        Directory.CreateDirectory(Path.GetDirectoryName(materializedVolumePath)!);
        File.Copy(originalVolumePath, materializedVolumePath, overwrite: true);

        await RemapAttachmentPathsInPlaceAsync(blendPath, [attachment]);
        await File.WriteAllTextAsync(blendPath + ".attachments.json", JsonSerializer.Serialize(new[] { attachment }));
        File.Delete(originalVolumePath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("external volume", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    #endregion

    #region Tools

    private async Task CreateBlendFileAsync(string blendPath, IEnumerable<string> pythonLines)
    {
        var scriptPath = Path.Combine(m_tempDirectory, $"create_library_volume_blend_{Guid.NewGuid():N}.py");
        var lines = new List<string>
        {
            "import bpy",
            "bpy.ops.wm.read_factory_settings(use_empty=True)"
        };

        lines.AddRange(pythonLines);
        lines.Add($"bpy.ops.wm.save_mainfile(filepath=r'{NormalizePythonPath(blendPath)}')");

        try
        {
            await File.WriteAllLinesAsync(scriptPath, lines);

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = GetBlenderExecutablePath(),
                    Arguments = $"-b --factory-startup --python-exit-code 1 --python \"{scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Blender library/volume scene creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
            }
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                try { File.Delete(scriptPath); }
                catch { }
            }
        }
    }

    private async Task RemapAttachmentPathsInPlaceAsync(string blendPath, IReadOnlyList<RenderSceneAttachmentRefData> attachments)
    {
        var helperType = typeof(BlenderRunner).Assembly.GetType("OutWit.Controller.Render.Utils.BlenderSceneAttachmentRemapHelper")
                         ?? throw new InvalidOperationException("Failed to resolve BlenderSceneAttachmentRemapHelper type.");
        var method = helperType.GetMethod("RemapAttachmentPathsInPlaceAsync", BindingFlags.Public | BindingFlags.Static)
                     ?? throw new InvalidOperationException("Failed to resolve RemapAttachmentPathsInPlaceAsync method.");

        var task = method.Invoke(null, [m_blenderRunner, blendPath, attachments, CancellationToken.None]) as Task;
        if (task == null)
            throw new InvalidOperationException("BlenderSceneAttachmentRemapHelper returned no task.");

        await task;
    }

    private string GetBlenderExecutablePath()
    {
        return typeof(BlenderRunner)
                   .GetField("m_blenderPath", BindingFlags.Instance | BindingFlags.NonPublic)?
                   .GetValue(m_blenderRunner) as string
               ?? throw new InvalidOperationException("Failed to resolve Blender executable path from BlenderRunner.");
    }

    private static void CreateDummyFile(string filePath)
    {
        File.WriteAllText(filePath, "outwit-test");
    }

    private static string NormalizePythonPath(string path)
    {
        return path.Replace("\\", "/").Replace("'", "\\'");
    }

    #endregion
}
