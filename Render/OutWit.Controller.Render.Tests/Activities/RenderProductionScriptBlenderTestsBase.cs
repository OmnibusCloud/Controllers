using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Shared setup and helpers for the RenderProductionScript* end-to-end test fixtures.
/// Each [TestFixture] subclass owns one thematic slice of the bundled-script test suite
/// (basic render / cube_diorama diagnostics / scene render / diagnostics+preflight /
/// validate-blend) while inheriting the Blender-prerequisite gating, engine reload,
/// blob-service plumbing and the small builders that all slices share.
/// </summary>
internal abstract class RenderProductionScriptBlenderTestsBase
{
    #region Constants

    protected const string TEST_BLEND_SUBPATH = "@Prerequisites/test_scene.blend";

    protected static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #endregion

    #region Fields

    protected string m_blobStoragePath = null!;
    protected string? m_blendPath;
    protected RenderTestBlobService m_blobService = null!;
    protected string? m_controllersPath;
    protected string? m_cubeDioramaBlendPath;
    protected string? m_scriptsPath;
    protected string? m_solutionRoot;
    protected IWitEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (m_solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(m_solutionRoot);
        if (blenderDir == null)
            Assert.Ignore("No supported Blender prerequisites for current OS/architecture");

        if (!new BlenderRunner(blenderDir, NullLogger.Instance).IsAvailable)
            Assert.Ignore($"Blender not found at {blenderDir}");

        m_controllersPath = RenderTestAssetPaths.FindControllersPath();
        if (m_controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_scriptsPath = RenderTestAssetPaths.FindBundledScriptsPath();
        if (m_scriptsPath == null)
            Assert.Ignore("@Scripts not found");

        m_blendPath = RenderTestAssetPaths.GetTestScenePath(m_solutionRoot);
        if (!File.Exists(m_blendPath))
            Assert.Ignore($"Test scene not found at {m_blendPath}");

        m_cubeDioramaBlendPath = RenderTestAssetPaths.GetCubeDioramaScenePath(m_solutionRoot);
        // cube_diorama existence is checked per-test (only ~3 tests need it) so the rest
        // of the fixture stays runnable when @Data/cube_diorama is absent locally.

        // Build the blob service + engine once per fixture. The blob service
        // is reused across all tests in the fixture and only resets its storage
        // path per [SetUp] — that lets us keep the engine's DI container intact
        // instead of reloading every controller plugin for each test, which
        // used to dominate test time at ~4s/test of plugin reload overhead.
        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_render_blobtest_init_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_blobStoragePath);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: m_controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: m_controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderTestNodesManager(WitEngineNodeSdk.Instance));
            });
    }

    protected void RequireCubeDiorama()
    {
        if (m_cubeDioramaBlendPath == null || !File.Exists(m_cubeDioramaBlendPath))
            Assert.Ignore($"Cube Diorama scene not found at {m_cubeDioramaBlendPath}");
    }

    [SetUp]
    public void SetUp()
    {
        var previousStoragePath = m_blobStoragePath;
        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_render_blobtest_{Guid.NewGuid():N}");
        m_blobService.Reset(m_blobStoragePath);

        // Drop the OneTimeSetUp's bootstrap storage dir on the first [SetUp].
        if (previousStoragePath != null && Directory.Exists(previousStoragePath) && previousStoragePath.Contains("witcloud_render_blobtest_init_"))
            Directory.Delete(previousStoragePath, recursive: true);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Tools

    protected static RenderSceneRefData CreateSceneRef(Guid sceneBlobId, IReadOnlyList<RenderSceneAttachmentRefData>? attachedFiles = null)
    {
        return new RenderSceneRefData
        {
            BlendBlobId = sceneBlobId,
            AttachedFiles = attachedFiles?.Select(me => (RenderSceneAttachmentRefData)me.Clone()).ToList() ?? []
        };
    }

    protected static async Task<RenderSceneData> CreateInlineSceneAsync(string blendFilePath)
    {
        return new RenderSceneData
        {
            FileName = Path.GetFileName(blendFilePath),
            BlendFileBytes = await File.ReadAllBytesAsync(blendFilePath)
        };
    }

    protected static RenderOptionsData CreateOptions(
        RenderEngine engine = RenderEngine.Cycles,
        int width = 64,
        int height = 64)
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = engine,
            Samples = 4,
            ResolutionX = width,
            ResolutionY = height
        };
    }

    protected static VideoOptionsData CreateVideoOptions()
    {
        return new VideoOptionsData
        {
            FrameRate = 24,
            ConstantRateFactor = 23
        };
    }

    protected static TileOptionsData CreateTileOptions(int overlapPx = 0, TileBlendMode blendMode = TileBlendMode.CenterPriorityCrop)
    {
        return new TileOptionsData
        {
            OverlapPx = overlapPx,
            BlendMode = blendMode
        };
    }

    protected static void AssertImageIsNotSolidBlack(string imagePath, string context)
    {
        using var image = Image.Load<Rgba32>(imagePath);

        long nonBlackPixels = 0;
        long totalPixels = (long)image.Width * image.Height;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0)
                    nonBlackPixels++;
            }
        }

        Assert.That(totalPixels, Is.GreaterThan(0), $"{context}: image contains no pixels.");
        Assert.That(nonBlackPixels, Is.GreaterThan(0), $"{context}: rendered image is completely black.");
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

    protected static double CalculateMeanAbsoluteRgbDifference(Image<Rgba32> left, Image<Rgba32> right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
            throw new InvalidOperationException($"Image diff requires matching dimensions, got {left.Width}x{left.Height} and {right.Width}x{right.Height}.");

        long totalDifference = 0;
        long totalPixels = (long)left.Width * left.Height;

        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                var leftPixel = left[x, y];
                var rightPixel = right[x, y];
                totalDifference += Math.Abs(leftPixel.R - rightPixel.R)
                                   + Math.Abs(leftPixel.G - rightPixel.G)
                                   + Math.Abs(leftPixel.B - rightPixel.B);
            }
        }

        return totalPixels == 0 ? 0 : totalDifference / (double)totalPixels;
    }

    protected string GetBlenderExecutablePath()
    {
        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(m_solutionRoot!)
                         ?? throw new InvalidOperationException("No supported Blender prerequisites for current OS/architecture.");
        var blenderRunner = new BlenderRunner(blenderDir, NullLogger.Instance);
        if (!blenderRunner.IsAvailable)
            throw new InvalidOperationException($"Blender not found at {blenderDir}");

        return typeof(BlenderRunner)
            .GetField("m_blenderPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .GetValue(blenderRunner) as string
            ?? throw new InvalidOperationException("Failed to resolve Blender executable path from BlenderRunner.");
    }

    protected async Task<RenderOptionsData> ReadSceneRenderOptionsAsync(string blendFilePath)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"render_scene_options_{Guid.NewGuid():N}.py");

        try
        {
            await File.WriteAllLinesAsync(scriptPath,
            [
                "import bpy, json",
                "scene = bpy.context.scene",
                "render = scene.render",
                "cycles = getattr(scene, 'cycles', None)",
                "percentage = max(1, int(render.resolution_percentage))",
                "payload = {",
                "  'resolution_x': int(render.resolution_x * percentage / 100),",
                "  'resolution_y': int(render.resolution_y * percentage / 100),",
                "  'samples': int(getattr(cycles, 'samples', 0) or 0) if cycles is not None else 0,",
                "  'denoise': bool(getattr(cycles, 'use_denoising', False)) if cycles is not None else False",
                "}",
                "print('WIT_DIAGNOSTICS_START')",
                "print(json.dumps(payload))",
                "print('WIT_DIAGNOSTICS_END')"
            ]);

            var args = $"-b \"{blendFilePath}\" --python-exit-code 1 --python \"{scriptPath}\"";

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = GetBlenderExecutablePath(),
                    Arguments = args,
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
                throw new InvalidOperationException($"Blender scene option diagnostics failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");

            const string startMarker = "WIT_DIAGNOSTICS_START";
            const string endMarker = "WIT_DIAGNOSTICS_END";
            var startIndex = stdout.IndexOf(startMarker, StringComparison.Ordinal);
            var endIndex = stdout.IndexOf(endMarker, StringComparison.Ordinal);
            if (startIndex < 0 || endIndex <= startIndex)
                throw new InvalidOperationException($"Blender scene option diagnostics markers were not found. Stdout: {stdout}\nStderr: {stderr}");

            var json = stdout.Substring(startIndex + startMarker.Length, endIndex - startIndex - startMarker.Length).Trim();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                Samples = root.GetProperty("samples").GetInt32(),
                ResolutionX = root.GetProperty("resolution_x").GetInt32(),
                ResolutionY = root.GetProperty("resolution_y").GetInt32(),
                Denoise = root.GetProperty("denoise").GetBoolean()
            };
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

    #endregion
}
