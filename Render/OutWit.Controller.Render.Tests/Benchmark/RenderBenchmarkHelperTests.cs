using System.Reflection;
using OutWit.Controller.Render;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Tests.Benchmark;

[TestFixture]
public sealed class RenderBenchmarkHelperTests
{
    #region Tests

    [TestCase(RenderEngine.Cycles, "benchmark-still-cycles@v1")]
    [TestCase(RenderEngine.Eevee, "benchmark-still-eevee@v1")]
    [TestCase(RenderEngine.GreasePencil, "benchmark-still-grease-pencil@v1")]
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
