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
public sealed class RenderValidateBlendTransferredMediaBlenderTests
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
        m_tempDirectory = Path.Combine(Path.GetTempPath(), $"witcloud_validate_media_{Guid.NewGuid():N}");
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
    public async Task ValidateBlendDetailedAsyncDoesNotReportExternalSoundDependencyAfterAttachmentRemapTest()
    {
        var originalSoundPath = Path.Combine(m_tempDirectory, "original_sound.wav");
        CreateTestWaveFile(originalSoundPath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_transferred_sound.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"sound = bpy.data.sounds.load(r'{NormalizePythonPath(originalSoundPath)}')",
                "sound.use_fake_user = True"
            ]);

        var attachment = new RenderSceneAttachmentRefData
        {
            Kind = "Sound",
            OriginalPath = originalSoundPath,
            RelativePath = "deps/sounds/transferred-sound.wav",
            PackagingStrategy = "SceneAttachmentBlob"
        };

        var materializedSoundPath = Path.Combine(m_tempDirectory, "deps", "sounds", "transferred-sound.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(materializedSoundPath)!);
        File.Copy(originalSoundPath, materializedSoundPath, overwrite: true);

        await RemapAttachmentPathsInPlaceAsync(blendPath, [attachment]);
        await File.WriteAllTextAsync(blendPath + ".attachments.json", JsonSerializer.Serialize(new[] { attachment }));
        File.Delete(originalSoundPath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("external sound", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncDoesNotReportExternalMovieClipDependencyAfterAttachmentRemapTest()
    {
        var clipDirectory = Path.Combine(m_tempDirectory, "clip-sequence");
        Directory.CreateDirectory(clipDirectory);
        var originalClipPath = Path.Combine(clipDirectory, "clip_0001.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(originalClipPath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_transferred_movie_clip.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"clip = bpy.data.movieclips.load(r'{NormalizePythonPath(originalClipPath)}')",
                "clip.use_fake_user = True"
            ]);

        var attachment = new RenderSceneAttachmentRefData
        {
            Kind = "MovieClip",
            OriginalPath = originalClipPath,
            RelativePath = "deps/movie-clips/transferred-clip.png",
            PackagingStrategy = "SceneAttachmentBlob"
        };

        var materializedClipPath = Path.Combine(m_tempDirectory, "deps", "movie-clips", "transferred-clip.png");
        Directory.CreateDirectory(Path.GetDirectoryName(materializedClipPath)!);
        File.Copy(originalClipPath, materializedClipPath, overwrite: true);

        await RemapAttachmentPathsInPlaceAsync(blendPath, [attachment]);
        await File.WriteAllTextAsync(blendPath + ".attachments.json", JsonSerializer.Serialize(new[] { attachment }));
        File.Delete(originalClipPath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("movie clip", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncDoesNotReportExternalVseMediaWarningsAfterAttachmentRemapTest()
    {
        var imageDirectory = Path.Combine(m_tempDirectory, "vse-image-strip");
        Directory.CreateDirectory(imageDirectory);
        var originalImagePath = Path.Combine(imageDirectory, "frame_0001.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(originalImagePath);

        var originalSoundPath = Path.Combine(m_tempDirectory, "vse-strip.wav");
        CreateTestWaveFile(originalSoundPath);
        var originalMoviePath = Path.Combine(m_tempDirectory, "vse-strip.mp4");
        await CreateTestVideoFileAsync(originalMoviePath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_transferred_vse_media.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "scene = bpy.context.scene",
                "editor = scene.sequence_editor_create()",
                $"editor.strips.new_image('Image Strip', r'{NormalizePythonPath(originalImagePath)}', 1, 1)",
                $"editor.strips.new_sound('Sound Strip', r'{NormalizePythonPath(originalSoundPath)}', 2, 1)",
                $"editor.strips.new_movie('Movie Strip', r'{NormalizePythonPath(originalMoviePath)}', 3, 1)"
            ]);

        var attachments = new[]
        {
            new RenderSceneAttachmentRefData
            {
                Kind = "VseImageStripFrame",
                OriginalPath = originalImagePath,
                RelativePath = "deps/vse/image-strips/Image_Strip/frame_0001.png",
                PackagingStrategy = "SceneAttachmentBlob"
            },
            new RenderSceneAttachmentRefData
            {
                Kind = "Sound",
                OriginalPath = originalSoundPath,
                RelativePath = "deps/sounds/vse-strip.wav",
                PackagingStrategy = "SceneAttachmentBlob"
            },
            new RenderSceneAttachmentRefData
            {
                Kind = "VseSoundStrip",
                OriginalPath = originalSoundPath,
                RelativePath = "deps/vse/sound-strips/Sound_Strip/vse-strip.wav",
                PackagingStrategy = "SceneAttachmentBlob"
            },
            new RenderSceneAttachmentRefData
            {
                Kind = "VseMovieStrip",
                OriginalPath = originalMoviePath,
                RelativePath = "deps/vse/movie-strips/Movie_Strip/vse-strip.mp4",
                PackagingStrategy = "SceneAttachmentBlob"
            }
        };

        await MaterializeAttachmentCopiesAsync(attachments);
        await RemapAttachmentPathsInPlaceAsync(blendPath, attachments);
        await File.WriteAllTextAsync(blendPath + ".attachments.json", JsonSerializer.Serialize(attachments));
        File.Delete(originalImagePath);
        File.Delete(originalSoundPath);
        File.Delete(originalMoviePath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("VSE image strip", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(validation.Warnings.Any(me => me.Contains("VSE sound strip", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(validation.Warnings.Any(me => me.Contains("VSE movie strip", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(validation.Warnings.Any(me => me.Contains("external sound", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    #endregion

    #region Tools

    private async Task CreateBlendFileAsync(string blendPath, IEnumerable<string> pythonLines)
    {
        var scriptPath = Path.Combine(m_tempDirectory, $"create_media_blend_{Guid.NewGuid():N}.py");
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
                    $"Blender media scene creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
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

    private async Task MaterializeAttachmentCopiesAsync(IEnumerable<RenderSceneAttachmentRefData> attachments)
    {
        foreach (var attachment in attachments)
        {
            var materializedPath = Path.Combine(m_tempDirectory, attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(materializedPath)!);
            File.Copy(attachment.OriginalPath, materializedPath, overwrite: true);
            await Task.CompletedTask;
        }
    }

    private async Task CreateTestVideoFileAsync(string filePath)
    {
        var ffmpegPath = ResolveFfmpegExecutablePath();
        if (!File.Exists(ffmpegPath))
            Assert.Ignore($"ffmpeg not found at {ffmpegPath}");

        var framePath = Path.Combine(Path.GetDirectoryName(filePath)!, $"video_frame_{Guid.NewGuid():N}.png");

        try
        {
            using (var image = new Image<Rgba32>(2, 2))
                await image.SaveAsPngAsync(framePath);

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -loop 1 -i \"{framePath}\" -t 1 -pix_fmt yuv420p \"{filePath}\"",
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
            if (process.ExitCode != 0 || !File.Exists(filePath))
                Assert.Ignore($"ffmpeg could not create a test video file. Stdout: {stdout} Stderr: {stderr}");
        }
        finally
        {
            if (File.Exists(framePath))
            {
                try { File.Delete(framePath); }
                catch { }
            }
        }
    }

    private static string ResolveFfmpegExecutablePath()
    {
        var resolverType = typeof(BlenderRunner).Assembly.GetType("OutWit.Controller.Render.Utils.RenderBinaryResolver")
                           ?? throw new InvalidOperationException("Failed to resolve RenderBinaryResolver type.");
        var resolveRootMethod = resolverType.GetMethod("ResolveFfmpegRoot", BindingFlags.Public | BindingFlags.Static)
                                ?? throw new InvalidOperationException("Failed to resolve ResolveFfmpegRoot method.");
        var resolvePathMethod = resolverType.GetMethod("ResolveFfmpegPath", BindingFlags.Public | BindingFlags.Static)
                                ?? throw new InvalidOperationException("Failed to resolve ResolveFfmpegPath method.");

        var ffmpegRoot = resolveRootMethod.Invoke(null, [typeof(BlenderRunner).Assembly.Location]) as string
                         ?? throw new InvalidOperationException("Failed to resolve ffmpeg root.");

        return resolvePathMethod.Invoke(null, [ffmpegRoot]) as string
               ?? throw new InvalidOperationException("Failed to resolve ffmpeg executable path.");
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

    private static void CreateTestWaveFile(string filePath)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleRate = 8000;
        const short samplesCount = 16;
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var byteRate = sampleRate * blockAlign;
        var dataLength = samplesCount * blockAlign;

        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        for (var index = 0; index < samplesCount; index++)
            writer.Write((short)0);
    }

    private static string NormalizePythonPath(string path)
    {
        return path.Replace("\\", "/").Replace("'", "\\'");
    }

    #endregion
}
