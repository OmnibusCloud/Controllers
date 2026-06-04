using System.Reflection;
using OutWit.Controller.Render;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Benchmark;

[TestFixture]
public sealed class RenderBenchmarkHelperTests
{
    #region Tests

    [TestCase(RenderEngine.Cycles, "benchmark-still-cycles@v2")]
    [TestCase(RenderEngine.Eevee, "benchmark-still-eevee@v2")]
    [TestCase(RenderEngine.GreasePencil, "benchmark-still-grease-pencil@v2")]
    public void GetFrameBenchmarkDatasetIdReturnsEngineSpecificDatasetTest(RenderEngine engine, string expectedDatasetId)
    {
        var datasetId = (string?)RenderBenchmarkHelperType
            .GetMethod("GetFrameBenchmarkDatasetId", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [engine]);

        Assert.That(datasetId, Is.EqualTo(expectedDatasetId));
    }

    [TestCase(RenderEngine.Cycles)]
    [TestCase(RenderEngine.Eevee)]
    [TestCase(RenderEngine.GreasePencil)]
    public void CreateBenchmarkRenderOptionsUsesRequestedEngineTest(RenderEngine engine)
    {
        var options = (RenderOptionsData?)RenderBenchmarkHelperType
            .GetMethod("CreateBenchmarkRenderOptions", BindingFlags.Public | BindingFlags.Static, binder: null, types: [typeof(RenderEngine)], modifiers: null)!
            .Invoke(null, [engine]);

        Assert.That(options, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(options!.Engine, Is.EqualTo(engine));
            Assert.That(options.Format, Is.EqualTo(RenderFormat.PNG));
            Assert.That(options.Samples, Is.GreaterThan(0));
            Assert.That(options.ResolutionX, Is.GreaterThan(0));
            Assert.That(options.ResolutionY, Is.GreaterThan(0));
            Assert.That(options.Denoise, Is.False);
        });
    }

    [TestCase("BENCHMARK_RENDER_RESOLUTION", 512)]
    [TestCase("BENCHMARK_RENDER_GRID", 4)]
    [TestCase("BENCHMARK_RENDER_SAMPLES", 128)]
    [TestCase("BENCHMARK_RENDER_MAX_BOUNCES", 8)]
    public void RenderBenchmarkCalibrationConstantsAreV2ValuesTest(string constName, int expected)
    {
        // Locks the v2 calibration (heavier 512px / 4×4-grid scene) chosen to stop the 256px
        // scene under-rating discrete GPUs vs an integrated M4 — see RenderBenchmarkHelper's
        // BENCHMARK_RENDER_* comment. A revert to the v1 values must fail loudly here.
        var field = RenderBenchmarkHelperType.GetField(constName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(field, Is.Not.Null, $"Calibration constant {constName} not found.");
        Assert.That(field!.GetValue(null), Is.EqualTo(expected));
    }

    [Test]
    public void CreateBenchmarkRenderOptionsWithoutEngineUsesLegacyCyclesDefaultTest()
    {
        var options = (RenderOptionsData?)RenderBenchmarkHelperType
            .GetMethod("CreateBenchmarkRenderOptions", BindingFlags.Public | BindingFlags.Static, binder: null, types: Type.EmptyTypes, modifiers: null)!
            .Invoke(null, null);

        Assert.That(options, Is.Not.Null);
        Assert.That(options!.Engine, Is.EqualTo(RenderEngine.Cycles));
    }

    #endregion

    #region Properties

    private static Type RenderBenchmarkHelperType => typeof(WitControllerRenderModule).Assembly.GetType("OutWit.Controller.Render.Utils.RenderBenchmarkHelper", throwOnError: true)!;

    #endregion
}
