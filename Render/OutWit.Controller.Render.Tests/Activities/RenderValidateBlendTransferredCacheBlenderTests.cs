using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class RenderValidateBlendTransferredCacheBlenderTests
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
        m_tempDirectory = Path.Combine(Path.GetTempPath(), $"witcloud_validate_cache_{Guid.NewGuid():N}");
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
    public async Task ValidateBlendDetailedAsyncReportsWarningForExistingCacheFileDependencyTest()
    {
        var cachePath = Path.Combine(m_tempDirectory, "external_cache.abc");
        CreateDummyFile(cachePath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_cache_file.blend");
        await CreateBlendFileWithCacheFileAsync(blendPath, cachePath, "ExternalCache");

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("external cache file", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [Test]
    public async Task ValidateBlendDetailedAsyncDoesNotReportExternalCacheFileDependencyAfterAttachmentRemapTest()
    {
        var originalCachePath = Path.Combine(m_tempDirectory, "original_cache.abc");
        CreateDummyFile(originalCachePath);

        var blendPath = Path.Combine(m_tempDirectory, "scene_with_transferred_cache_file.blend");
        await CreateBlendFileWithCacheFileAsync(blendPath, originalCachePath, "TransferredCache");

        var attachment = new RenderSceneAttachmentRefData
        {
            Kind = "CacheFile",
            OriginalPath = originalCachePath,
            RelativePath = "deps/cache-files/transferred-cache.abc",
            PackagingStrategy = "SceneAttachmentBlob"
        };

        var materializedCachePath = Path.Combine(m_tempDirectory, "deps", "cache-files", "transferred-cache.abc");
        Directory.CreateDirectory(Path.GetDirectoryName(materializedCachePath)!);
        File.Copy(originalCachePath, materializedCachePath, overwrite: true);

        await RemapAttachmentPathsInPlaceAsync(blendPath, [attachment]);
        await File.WriteAllTextAsync(blendPath + ".attachments.json", JsonSerializer.Serialize(new[] { attachment }));
        File.Delete(originalCachePath);

        var validation = await m_blenderRunner.ValidateBlendDetailedAsync(blendPath);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("external cache file", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    #endregion

    #region Tools

    private async Task CreateBlendFileWithCacheFileAsync(string blendPath, string cachePath, string cacheName)
    {
        try
        {
            await CreateBlendFileAsync(blendPath, BuildCacheFilePythonLines(cachePath, cacheName));
        }
        catch (InvalidOperationException e) when (e.Message.Contains("CacheFile datablock creation is unavailable", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore("The current Blender runtime does not expose a stable cache_file creation path for tests.");
        }
    }

    private async Task CreateBlendFileAsync(string blendPath, IEnumerable<string> pythonLines)
    {
        var scriptPath = Path.Combine(m_tempDirectory, $"create_cache_blend_{Guid.NewGuid():N}.py");
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
                    $"Blender cache scene creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
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

    private static IReadOnlyList<string> BuildCacheFilePythonLines(string cachePath, string cacheName)
    {
        return
        [
            $"cache_path = r'{NormalizePythonPath(cachePath)}'",
            $"cache_name = '{NormalizePythonPath(cacheName)}'",
            "cache_file = None",
            "cache_files = getattr(bpy.data, 'cache_files', None)",
            "load_fn = getattr(cache_files, 'load', None) if cache_files is not None else None",
            "if callable(load_fn):",
            "    try:",
            "        cache_file = load_fn(cache_path)",
            "    except Exception:",
            "        cache_file = None",
            "if cache_file is None:",
            "    new_fn = getattr(cache_files, 'new', None) if cache_files is not None else None",
            "    if callable(new_fn):",
            "        try:",
            "            cache_file = new_fn(cache_name)",
            "            cache_file.filepath = cache_path",
            "        except Exception:",
            "            cache_file = None",
            "if cache_file is None:",
            "    open_op = getattr(getattr(bpy.ops, 'cachefile', None), 'open', None)",
            "    if callable(open_op):",
            "        try:",
            "            open_op(filepath=cache_path)",
            "            cache_file = list(getattr(bpy.data, 'cache_files', []))[-1] if len(getattr(bpy.data, 'cache_files', [])) > 0 else None",
            "        except Exception:",
            "            cache_file = None",
            "if cache_file is None:",
            "    raise RuntimeError('CacheFile datablock creation is unavailable in this Blender runtime.')",
            "cache_file.use_fake_user = True"
        ];
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
