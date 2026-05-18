using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class RenderValidateBlendTransferredImageSequenceBlenderTests
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
        m_tempDirectory = Path.Combine(Path.GetTempPath(), $"witcloud_validate_image_sequence_{Guid.NewGuid():N}");
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
    public async Task ValidateBlendDetailedAsyncDoesNotReportImageSequenceWarningAfterAttachmentRemapTest()
    {
        var originalSequenceDirectory = Path.Combine(m_tempDirectory, "original-sequence");
        Directory.CreateDirectory(originalSequenceDirectory);
        var originalFramePaths = await CreateSequenceFramesAsync(originalSequenceDirectory, "plate", 2);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_transferred_image_sequence.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(originalFramePaths[0])}')",
                "image.source = 'SEQUENCE'",
                "image.use_fake_user = True"
            ]);

        var attachments = originalFramePaths
            .Select(me => new RenderSceneAttachmentRefData
            {
                Kind = "ImageSequenceFrame",
                OriginalPath = me,
                RelativePath = $"deps/image-sequences/Plate/{Path.GetFileName(me)}",
                PackagingStrategy = "SceneAttachmentBlob"
            })
            .ToArray();

        await MaterializeAttachmentCopiesAsync(attachments);
        await RemapAttachmentPathsInPlaceAsync(blendPath, attachments);
        await File.WriteAllTextAsync(blendPath + ".attachments.json", JsonSerializer.Serialize(attachments));
        Directory.Delete(originalSequenceDirectory, recursive: true);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("image sequence", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    #endregion

    #region Tools

    private async Task<string[]> CreateSequenceFramesAsync(string directory, string prefix, int frameCount)
    {
        var result = new string[frameCount];
        for (var index = 0; index < frameCount; index++)
        {
            var framePath = Path.Combine(directory, $"{prefix}_{index + 1:0000}.png");
            using var image = new Image<Rgba32>(1, 1);
            await image.SaveAsPngAsync(framePath);
            result[index] = framePath;
        }

        return result;
    }

    private async Task MaterializeAttachmentCopiesAsync(IReadOnlyList<RenderSceneAttachmentRefData> attachments)
    {
        foreach (var attachment in attachments)
        {
            var materializedPath = Path.Combine(m_tempDirectory, attachment.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(materializedPath)!);
            File.Copy(attachment.OriginalPath, materializedPath, overwrite: true);
        }
    }

    private async Task CreateBlendFileAsync(string blendPath, IEnumerable<string> pythonLines)
    {
        var scriptPath = Path.Combine(m_tempDirectory, $"create_image_sequence_blend_{Guid.NewGuid():N}.py");
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
                    $"Blender image-sequence scene creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
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

    private static string NormalizePythonPath(string path)
    {
        return path.Replace("\\", "/").Replace("'", "\\'");
    }

    #endregion
}
