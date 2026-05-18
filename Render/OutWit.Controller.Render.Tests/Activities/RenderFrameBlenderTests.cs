using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Integration tests that run a full Render.Frame processing pipeline with real Blender.
/// Requires Blender portable in @Prerequisites and test .blend scene.
/// Marked [Explicit] — run manually, not in CI.
/// </summary>
[TestFixture]
[Explicit("Requires Blender installation in @Prerequisites")]
public class RenderFrameBlenderTests
{
    #region Constants

    private const string TEST_BLEND_SUBPATH = "@Prerequisites/test_scene.blend";

    #endregion

    #region Fields

    private string m_outputDir = null!;
    private string? m_solutionRoot;
    private string? m_blenderDir;
    private string? m_blendPath;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_solutionRoot = FindSolutionRoot();
        if (m_solutionRoot == null)
            Assert.Ignore("Solution root not found");

        m_blenderDir = ResolveBlenderDir(m_solutionRoot);
        if (m_blenderDir == null)
            Assert.Ignore("No supported Blender prerequisites for current OS/architecture");

        m_blendPath = Path.Combine(m_solutionRoot, TEST_BLEND_SUBPATH);

        if (!File.Exists(m_blendPath))
            Assert.Ignore($"Test scene not found at {m_blendPath}");

        var runner = new BlenderRunner(m_blenderDir, NullLogger.Instance);
        if (!runner.IsAvailable)
            Assert.Ignore($"Blender not found at {m_blenderDir}");

        // Init WitEngine with all controllers
        var controllersPath = FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        WitEngineSdk.Instance.Reload(false, null, controllersPath);
    }

    [SetUp]
    public void SetUp()
    {
        m_outputDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_outputDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_outputDir))
            Directory.Delete(m_outputDir, recursive: true);
    }

    #endregion

    #region Split + Frame Pipeline Tests

    [Test]
    public async Task SplitThenRenderSingleFrameTest()
    {
        // 1. Run Split to generate tasks
        var splitScript = """
                          Job:SplitJob(Blob:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
                          {
                              RenderTaskCollection:tasks = Render.Split(scene, startFrame, endFrame, options);
                          }
                          """;

        var splitJob = WitEngineSdk.Instance.Compile(splitScript);
        var sceneId = Guid.NewGuid(); // fake blobId — we'll use real path below
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };

        var splitStatus = await WitEngineSdk.Instance.ScheduleAndWaitAsync(splitJob, sceneId, 1, 3, options);
        Assert.That(splitStatus.Result, Is.EqualTo(WitProcessingResult.Completed));

        var tasks = splitJob.Variables["tasks"].Value as IReadOnlyList<RenderTaskData?>;
        Assert.That(tasks, Is.Not.Null);
        Assert.That(tasks!.Count, Is.EqualTo(3));

        // 2. Render each task with real Blender
        var runner = new BlenderRunner(m_blenderDir!, NullLogger.Instance);
        Assert.That(runner.IsAvailable, Is.True);

        var renderedPaths = new List<string>();

        foreach (var task in tasks)
        {
            Assert.That(task, Is.Not.Null);

            var outputBase = Path.Combine(m_outputDir, $"task_{task!.TaskIndex:D4}_");
            var renderedPath = await runner.RenderFrameAsync(
                m_blendPath!, task.Frame, outputBase, task.Options);

            Assert.That(File.Exists(renderedPath), Is.True);
            Assert.That(new FileInfo(renderedPath).Length, Is.GreaterThan(0));

            renderedPaths.Add(renderedPath);
        }

        Assert.That(renderedPaths, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task FullRenderFramesScriptServerOnlyTest()
    {
        // Full script with Split + Collect (no Grid.ForEach — server-only with Transform.ForEach)
        // This tests that the types flow correctly end-to-end
        var script = """
                     Job:ServerRender(Blob:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         RenderTaskCollection:tasks = Render.Split(scene, startFrame, endFrame, options);
                     }
                     """;

        var job = WitEngineSdk.Instance.Compile(script);
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };

        var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job, Guid.NewGuid(), 1, 3, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var tasks = job.Variables["tasks"].Value as IReadOnlyList<RenderTaskData?>;
        Assert.That(tasks, Is.Not.Null);
        Assert.That(tasks!.Count, Is.EqualTo(3));

        // Verify each task has correct options
        foreach (var task in tasks)
        {
            Assert.That(task!.Options.Samples, Is.EqualTo(4));
            Assert.That(task.Options.ResolutionX, Is.EqualTo(64));
            Assert.That(task.Options.Format, Is.EqualTo(RenderFormat.PNG));
        }
    }

    #endregion

    #region Benchmark Tests

    [Test]
    public async Task BlenderBenchmarkProducesValidRateTest()
    {
        var runner = new BlenderRunner(m_blenderDir!, NullLogger.Instance);

        // Use our test scene as benchmark scene
        var benchmarkBlend = m_blendPath!;
        var benchmarkOptions = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };

        var outputBase = Path.Combine(m_outputDir, "bench_");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var renderedPath = await runner.RenderFrameAsync(benchmarkBlend, 1, outputBase, benchmarkOptions);
        sw.Stop();

        Assert.That(File.Exists(renderedPath), Is.True);

        double totalPixels = 64.0 * 64 * 4;
        double pixelsPerSecond = totalPixels / sw.Elapsed.TotalSeconds;

        Assert.That(pixelsPerSecond, Is.GreaterThan(0));
        Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(30), "Benchmark should complete in under 30 seconds");

        TestContext.Out.WriteLine($"Benchmark: {pixelsPerSecond:F0} pixels/sec ({sw.Elapsed.TotalSeconds:F2}s)");
    }

    #endregion

    #region Tools

    private static string? FindSolutionRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "OutWit.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? FindControllersPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "@Controllers", "Debug");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? ResolveBlenderDir(string solutionRoot)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return Path.Combine(solutionRoot, "@Prerequisites", "blender");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return Path.Combine(solutionRoot, "@Prerequisites", "blender");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return Path.Combine(solutionRoot, "@Prerequisites", "blender");

        return null;
    }

    #endregion
}
