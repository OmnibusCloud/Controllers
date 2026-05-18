using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Sdk;
using OutWit.Controller.Render.Tests.Utils;

namespace OutWit.Controller.Render.Tests.Benchmark;

/// <summary>
/// Integration tests for real node-side Render benchmarks.
/// These tests load the packaged debug controller module and execute the benchmark path that runs real Blender tooling.
/// </summary>
[TestFixture]
[Category("Integration")]
public class RenderBenchmarkIntegrationTests
{
    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers\\Debug was not found.");

        var renderModulePath = Path.Combine(controllersPath, "render.module");
        if (!Directory.Exists(renderModulePath))
            Assert.Ignore("Render controller module output was not found. Rebuild OutWit.Controller.Render first.");

        var benchmarkRootPath = RenderTestAssetPaths.GetBenchmarkRootPath(solutionRoot);
        if (!Directory.Exists(benchmarkRootPath))
            Assert.Ignore($"Canonical benchmark prerequisite folder was not found: {benchmarkRootPath}");

        Assert.That(File.Exists(RenderTestAssetPaths.GetBenchmarkScenePath(solutionRoot)), Is.True,
            "Canonical benchmark scene is missing.");
        Assert.That(File.Exists(RenderTestAssetPaths.GetBenchmarkStillScenePath(solutionRoot)), Is.True,
            "Canonical still benchmark scene is missing.");
        Assert.That(File.Exists(RenderTestAssetPaths.GetBenchmarkVideoScenePath(solutionRoot)), Is.True,
            "Canonical video benchmark scene is missing.");

        Assert.That(File.Exists(Path.Combine(renderModulePath, "benchmark_scene.blend")), Is.True,
            "Packaged render module benchmark scene is missing.");
        Assert.That(File.Exists(Path.Combine(renderModulePath, "benchmark_scene_still.blend")), Is.True,
            "Packaged render module still benchmark scene is missing.");
        Assert.That(File.Exists(Path.Combine(renderModulePath, "benchmark_scene_video.blend")), Is.True,
            "Packaged render module video benchmark scene is missing.");

        WitEngineNodeSdk.Instance.Reload(useIsolatedContext: false, moduleFolder: controllersPath);
    }

    #endregion

    #region Tests

    public static IEnumerable<TestCaseData> BenchmarkCases()
    {
        yield return new TestCaseData("Render.Frame", "render-pixels@v1", "benchmark-still@v1");
        yield return new TestCaseData("Render.Frame.Cycles", "render-pixels@v1", "benchmark-still-cycles@v1");
        yield return new TestCaseData("Render.Frame.Eevee", "render-pixels@v1", "benchmark-still-eevee@v1");
        yield return new TestCaseData("Render.Frame.GreasePencil", "render-pixels@v1", "benchmark-still-grease-pencil@v1");
        yield return new TestCaseData("Render.BlenderVersion", "version-checks@v1", "runtime-diagnostics@v1");
        yield return new TestCaseData("Render.RuntimeDiagnostics", "runtime-diagnostics@v1", "runtime-diagnostics@v1");
        yield return new TestCaseData("Render.ValidateBlend", "blend-validations@v1", "benchmark-still@v1");
        yield return new TestCaseData("Render.PreflightFrames", "frame-preflights@v1", "preflight-frames@v1");
        yield return new TestCaseData("Render.PreflightStillTiled", "tiled-preflights@v1", "tiled-still@v1");
        yield return new TestCaseData("Render.PreflightVideo", "video-preflights@v1", "preflight-video@v1");
        yield return new TestCaseData("Render.Preflight", "unified-preflights@v1", "preflight-unified@v1");
    }

    [TestCaseSource(nameof(BenchmarkCases))]
    public async Task RunBenchmarkReturnsRealResultTest(string activityName, string expectedUnit, string expectedDatasetId)
    {
        var benchmarkOptions = (WitBenchmarkOptions)WitBenchmarkOptions.Default;
        var result = await WitEngineNodeSdk.Instance.RunBenchmark(activityName, benchmarkOptions);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Unit, Is.EqualTo(expectedUnit), $"Unexpected benchmark unit for {activityName}.");
        Assert.That(result.DatasetId, Is.EqualTo(expectedDatasetId), $"Unexpected benchmark dataset id for {activityName}.");
        Assert.That(result.Iterations, Is.GreaterThan(0), $"Benchmark iterations must be positive for {activityName}.");
        Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero), $"Benchmark elapsed time must be positive for {activityName}.");
        Assert.That(result.Rate, Is.GreaterThan(0), $"Benchmark rate must be positive for {activityName}.");
        Assert.That(result.Unit, Is.Not.EqualTo("task@v0"), $"{activityName} is still returning the placeholder benchmark unit.");
        Assert.That(
            result.Elapsed >= benchmarkOptions.MinDuration || result.Iterations > 1,
            Is.True,
            $"{activityName} benchmark should either run across multiple iterations or accumulate at least the configured minimum benchmark duration.");

        TestContext.Out.WriteLine($"{activityName}: {result.Rate:N2} {result.Unit}, iterations={result.Iterations}, dataset={result.DatasetId}");
    }

    [Test]
    public void RenderControllerIsLoadedIntoNodeTest()
    {
        var registeredActivities = WitEngineNodeSdk.Instance.RegisteredActivities;

        Assert.That(registeredActivities, Does.Contain("Render.Frame"), "Render.Frame was not registered in WitEngineNode.");
        Assert.That(registeredActivities, Does.Contain("Render.Frame.Cycles"), "Render.Frame.Cycles was not registered in WitEngineNode.");
        Assert.That(registeredActivities, Does.Contain("Render.Frame.Eevee"), "Render.Frame.Eevee was not registered in WitEngineNode.");
        Assert.That(registeredActivities, Does.Contain("Render.Frame.GreasePencil"), "Render.Frame.GreasePencil was not registered in WitEngineNode.");
        Assert.That(registeredActivities, Does.Contain("Render.PreflightVideo"), "Render.PreflightVideo was not registered in WitEngineNode.");
        Assert.That(registeredActivities, Does.Contain("Render.ValidateBlend"), "Render.ValidateBlend was not registered in WitEngineNode.");
    }

    #endregion
}
