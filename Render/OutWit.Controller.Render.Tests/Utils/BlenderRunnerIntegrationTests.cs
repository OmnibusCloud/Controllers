using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Utils;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Variables;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Integration tests that run real Blender from @Prerequisites.
/// These tests require Blender to be extracted at the expected path.
/// </summary>
[TestFixture]
public class BlenderRunnerIntegrationTests
{
    #region Constants

    private const string TEST_BLEND_SUBPATH = "@Prerequisites/test_scene.blend";

    #endregion

    #region Fields

    private BlenderRunner m_runner = null!;
    private string m_outputDir = null!;
    private string m_blendPath = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found — cannot locate @Prerequisites");

        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(solutionRoot);
        if (blenderDir == null)
            Assert.Ignore("No supported Blender prerequisites for current OS/architecture");

        m_blendPath = RenderTestAssetPaths.GetTestScenePath(solutionRoot);

        if (!File.Exists(m_blendPath))
            Assert.Ignore($"Test scene not found at {m_blendPath} — skip integration tests");

        m_runner = new BlenderRunner(blenderDir, NullLogger.Instance);
        if (!m_runner.IsAvailable)
            Assert.Ignore($"Blender not found at {blenderDir} — skip integration tests");
    }

    [SetUp]
    public void SetUp()
    {
        m_outputDir = Path.Combine(Path.GetTempPath(), $"witcloud_blender_inttest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_outputDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_outputDir))
            Directory.Delete(m_outputDir, recursive: true);
    }

    #endregion

    #region Version Tests

    [Test]
    public async Task GetVersionReturnsValidStringTest()
    {
        var version = await m_runner.GetVersionAsync();

        Assert.That(version, Does.StartWith("Blender"));
        Assert.That(version, Does.Not.Contain("Unknown"));
    }

    [Test]
    public void IsAvailableReturnsTrueTest()
    {
        Assert.That(m_runner.IsAvailable, Is.True);
    }

    #endregion

    #region Render Frame Tests

    [Test]
    public async Task RenderSingleFramePngTest()
    {
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };

        var outputBase = Path.Combine(m_outputDir, "render_");
        var renderedPath = await m_runner.RenderFrameAsync(m_blendPath, 1, outputBase, options);

        Assert.That(File.Exists(renderedPath), Is.True, $"Rendered file not found: {renderedPath}");
        Assert.That(new FileInfo(renderedPath).Length, Is.GreaterThan(0));

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found for golden-file validation.");
        RenderGoldenFileAssert.AssertImageMatches(renderedPath, solutionRoot, "RenderDirectStill", RenderEngine.Cycles, 64, 64);
    }

    [Test]
    public async Task RenderBenchmarkSceneMatchesGoldenFileTest()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found for benchmark golden validation.");
        var benchmarkBlendPath = RenderTestAssetPaths.GetBenchmarkScenePath(solutionRoot);
        if (!File.Exists(benchmarkBlendPath))
            Assert.Ignore($"Benchmark scene not found at {benchmarkBlendPath}");

        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };

        var outputBase = Path.Combine(m_outputDir, "benchmark_");
        var renderedPath = await m_runner.RenderFrameAsync(benchmarkBlendPath, 1, outputBase, options);

        RenderGoldenFileAssert.AssertImageMatches(renderedPath, solutionRoot, "BenchmarkScene", RenderEngine.Cycles, 64, 64);
    }

    [Test]
    public async Task RenderSingleFrameJpegTest()
    {
        var options = new RenderOptionsData
        {
            Format = RenderFormat.JPEG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };

        var outputBase = Path.Combine(m_outputDir, "render_");
        var renderedPath = await m_runner.RenderFrameAsync(m_blendPath, 1, outputBase, options);

        Assert.That(File.Exists(renderedPath), Is.True);
        Assert.That(renderedPath, Does.EndWith(".jpg"));
    }

    [Test]
    public async Task RenderMultipleFramesTest()
    {
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };

        var renderedPaths = new List<string>();
        for (int frame = 1; frame <= 3; frame++)
        {
            var outputBase = Path.Combine(m_outputDir, $"frame{frame}_");
            var path = await m_runner.RenderFrameAsync(m_blendPath, frame, outputBase, options);
            renderedPaths.Add(path);
        }

        Assert.That(renderedPaths, Has.Count.EqualTo(3));
        foreach (var path in renderedPaths)
        {
            Assert.That(File.Exists(path), Is.True, $"Missing: {path}");
        }
    }

    [Test]
    public async Task RenderWithCustomResolutionTest()
    {
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 2,
            ResolutionX = 32,
            ResolutionY = 32
        };

        var outputBase = Path.Combine(m_outputDir, "small_");
        var renderedPath = await m_runner.RenderFrameAsync(m_blendPath, 1, outputBase, options);

        Assert.That(File.Exists(renderedPath), Is.True);
        // 32x32 PNG should be very small
        Assert.That(new FileInfo(renderedPath).Length, Is.LessThan(50_000));
    }

    [Test]
    public async Task RenderWithDenoiseTest()
    {
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64,
            Denoise = true
        };

        var outputBase = Path.Combine(m_outputDir, "denoised_");
        var renderedPath = await m_runner.RenderFrameAsync(m_blendPath, 1, outputBase, options);

        Assert.That(File.Exists(renderedPath), Is.True);
    }

    #endregion

    #region Cancellation Tests

    [Test]
    public void RenderCancellationTest()
    {
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 1000, // many samples = slow render
            ResolutionX = 256,
            ResolutionY = 256
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var outputBase = Path.Combine(m_outputDir, "cancel_");

        var ex = Assert.CatchAsync<OperationCanceledException>(async () =>
            await m_runner.RenderFrameAsync(m_blendPath, 1, outputBase, options, cts.Token));

        Assert.That(ex, Is.Not.Null);
    }

    #endregion

    #region Error Tests

    [Test]
    public void RenderNonExistentBlendFileThrowsTest()
    {
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles
        };

        var outputBase = Path.Combine(m_outputDir, "missing_");

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await m_runner.RenderFrameAsync("/nonexistent/scene.blend", 1, outputBase, options));
    }

    #endregion
}
