using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.Benchmark;

namespace OutWit.Controller.Render.Tests.Benchmark;

/// <summary>
/// Real-hardware integration tests for the redesigned render benchmark
/// (<see cref="RenderBenchmarkHelper.MeasureRenderAsync"/> /
/// <see cref="BlenderRunner.RunBenchmarkRenderAsync"/>). Verifies that the benchmark:
/// renders a procedural compute-bound scene in one process, returns real render-only
/// timing (not Blender startup), records the device actually used in
/// <see cref="WitBenchmarkResult.Custom"/>, and — when a GPU backend is present — actually
/// uses the GPU (the property that makes the rate hardware-discriminating for Grid).
///
/// Requires the portable Blender install under <c>@Prerequisites/blender</c>; skips otherwise.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class RenderBenchmarkRenderIntegrationTests
{
    #region Fields

    private string? m_blenderDir;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found.");

        m_blenderDir = RenderTestAssetPaths.ResolveBlenderDir(solutionRoot!);
        if (m_blenderDir == null)
            Assert.Ignore("No Blender prerequisite for this OS/architecture.");

        if (!new BlenderRunner(m_blenderDir, NullLogger.Instance).IsAvailable)
            Assert.Ignore($"Blender not found at {m_blenderDir}.");
    }

    #endregion

    #region Tests

    [TestCase(RenderEngine.Cycles)]
    [TestCase(RenderEngine.Eevee)]
    [TestCase(RenderEngine.GreasePencil)]
    public async Task RenderBenchmarkProducesRealRenderOnlyTimingTest(RenderEngine engine)
    {
        var runner = new BlenderRunner(m_blenderDir!, NullLogger.Instance);

        var result = await RenderBenchmarkHelper.MeasureRenderAsync(
            runner,
            engine,
            WitBenchmarkOptions.Default,
            unit: RenderBenchmarkHelper.FRAME_UNIT,
            datasetId: RenderBenchmarkHelper.GetFrameBenchmarkDatasetId(engine),
            cancellationToken: CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Unit, Is.EqualTo(RenderBenchmarkHelper.FRAME_UNIT));
        Assert.That(result.Rate, Is.GreaterThan(0), "Render benchmark rate must be positive.");
        Assert.That(result.Iterations, Is.GreaterThan(0), "At least one frame must have been rendered inside the timed loop.");
        Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero), "Render-only elapsed time must be positive.");

        Assert.That(result.Custom, Is.Not.Null, "Render benchmark must record device metadata in Custom.");
        var custom = result.Custom!;
        Assert.That(custom.ContainsKey(RenderBenchmarkHelper.CUSTOM_RENDER_DEVICE), Is.True);
        Assert.That(custom[RenderBenchmarkHelper.CUSTOM_RENDER_DEVICE], Is.AnyOf("GPU", "CPU"));
        Assert.That(custom[RenderBenchmarkHelper.CUSTOM_RENDER_RESOLUTION], Is.EqualTo("512x512"));
        Assert.That(custom[RenderBenchmarkHelper.CUSTOM_RENDER_SAMPLES], Is.EqualTo("128"));
        Assert.That(custom[RenderBenchmarkHelper.CUSTOM_RENDER_FRAMES], Is.EqualTo(result.Iterations.ToString()));

        TestContext.Out.WriteLine(
            $"{engine}: rate={result.Rate:N0} {result.Unit}, frames={result.Iterations}, " +
            $"render-only={result.Elapsed.TotalSeconds:N2}s, device={custom[RenderBenchmarkHelper.CUSTOM_RENDER_DEVICE]}, " +
            $"backend={custom[RenderBenchmarkHelper.CUSTOM_RENDER_BACKEND]}, available={custom[RenderBenchmarkHelper.CUSTOM_AVAILABLE_BACKENDS]}");
    }

    [Test]
    public async Task CyclesBenchmarkUsesGpuWhenAvailableTest()
    {
        var runner = new BlenderRunner(m_blenderDir!, NullLogger.Instance);

        var result = await RenderBenchmarkHelper.MeasureRenderAsync(
            runner,
            RenderEngine.Cycles,
            WitBenchmarkOptions.Default,
            unit: RenderBenchmarkHelper.FRAME_UNIT,
            datasetId: RenderBenchmarkHelper.GetFrameBenchmarkDatasetId(RenderEngine.Cycles),
            cancellationToken: CancellationToken.None);

        var available = result.Custom![RenderBenchmarkHelper.CUSTOM_AVAILABLE_BACKENDS];
        if (available == "none")
            Assert.Ignore("No GPU backend available on this machine — GPU-use assertion not applicable.");

        Assert.That(
            result.Custom![RenderBenchmarkHelper.CUSTOM_RENDER_DEVICE],
            Is.EqualTo("GPU"),
            $"A GPU backend is available ({available}) but Cycles benchmarked on CPU.");
    }

    #endregion
}
