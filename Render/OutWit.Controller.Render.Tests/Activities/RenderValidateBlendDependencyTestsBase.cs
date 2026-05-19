using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Shared scaffolding for the RenderValidateBlendDependency* test fixtures.
/// Each [TestFixture] subclass owns one thematic slice of the dependency-validation
/// suite (external media files / library + simulation fixtures) while inheriting
/// the Blender-prerequisite gating, temp-dir scaffolding and the small set of
/// blend-file authoring helpers that all slices share.
/// </summary>
public abstract class RenderValidateBlendDependencyTestsBase
{
    #region Fields

    protected BlenderRunner m_blenderRunner = null!;

    protected string m_tempDirectory = null!;

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

        var udimScenePath = Path.Combine(solutionRoot, "@Data", "UDIM_monster", "udim-monster.blend");
        if (!File.Exists(udimScenePath))
            Assert.Ignore($"UDIM monster scene not found at {udimScenePath}");
    }

    [SetUp]
    public void SetUp()
    {
        m_tempDirectory = Path.Combine(Path.GetTempPath(), $"witcloud_validate_blend_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_tempDirectory))
            Directory.Delete(m_tempDirectory, recursive: true);
    }

    #endregion

    #region Tools

    protected async Task CreateBlendFileAsync(string blendPath, params string[] pythonLines)
    {
        await CreateBlendFileAsync(blendPath, (IEnumerable<string>)pythonLines);
    }

    protected async Task CreateBlendFileAsync(string blendPath, IEnumerable<string> pythonLines)
    {
        var scriptPath = Path.Combine(m_tempDirectory, $"create_blend_{Guid.NewGuid():N}.py");
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
                    $"Blender scene creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
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

    protected async Task CreatePackedBlendCopyAsync(string sourceBlendPath, string packedBlendPath)
    {
        var scriptPath = Path.Combine(m_tempDirectory, $"pack_blend_{Guid.NewGuid():N}.py");
        var lines = new[]
        {
            "import bpy",
            $"bpy.ops.wm.open_mainfile(filepath=r'{NormalizePythonPath(sourceBlendPath)}')",
            "bpy.ops.file.pack_all()",
            $"bpy.ops.wm.save_as_mainfile(filepath=r'{NormalizePythonPath(packedBlendPath)}', copy=True)"
        };

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
                    $"Blender packed-copy creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
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

    protected string GetBlenderExecutablePath()
    {
        return typeof(BlenderRunner)
                   .GetField("m_blenderPath", BindingFlags.Instance | BindingFlags.NonPublic)?
                   .GetValue(m_blenderRunner) as string
               ?? throw new InvalidOperationException("Failed to resolve Blender executable path from BlenderRunner.");
    }

    protected static string? FindTestFontPath()
    {
        var candidateDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            "/System/Library/Fonts",
            "/Library/Fonts"
        };

        foreach (var directory in candidateDirectories.Where(Directory.Exists))
        {
            try
            {
                var fontPath = Directory.EnumerateFiles(directory, "*.ttf", SearchOption.AllDirectories).FirstOrDefault()
                               ?? Directory.EnumerateFiles(directory, "*.otf", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(fontPath))
                    return fontPath;
            }
            catch
            {
                // Ignore inaccessible font folders and continue probing.
            }
        }

        return null;
    }

    protected static void CreateTestWaveFile(string filePath)
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

    protected static void CreateDummyFile(string filePath)
    {
        File.WriteAllText(filePath, "outwit-test");
    }

    protected static void AssertSimulationFixtureBlocked(RenderValidateBlendData validation)
    {
        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues, Is.Not.Empty);
            Assert.That(
                validation.Issues.Any(me => me.Contains("baked simulation data", StringComparison.OrdinalIgnoreCase)
                                            || me.Contains("baked mesh cache", StringComparison.OrdinalIgnoreCase)
                                            || me.Contains("cache directory", StringComparison.OrdinalIgnoreCase)
                                            || me.Contains("simulation", StringComparison.OrdinalIgnoreCase)),
                Is.True);
        });
    }

    protected static string NormalizePythonPath(string path)
    {
        return path.Replace("\\", "/").Replace("'", "\\'");
    }

    #endregion
}
