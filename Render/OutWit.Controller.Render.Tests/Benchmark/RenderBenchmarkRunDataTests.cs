using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Benchmark;

/// <summary>
/// Unit tests for <see cref="RenderBenchmarkRunData.ComputeRate"/> — the throughput value the Grid
/// allocator consumes as a node's render <c>Rate</c>. The rate must be
/// <c>resX·resY·samples·frames / renderSeconds</c> (pixel-samples per render-second) and must guard
/// against a non-positive render time.
/// </summary>
[TestFixture]
public sealed class RenderBenchmarkRunDataTests
{
    #region Tests

    [Test]
    public void ComputeRateReturnsPixelSamplesPerSecondTest()
    {
        var data = new RenderBenchmarkRunData
        {
            Engine = RenderEngine.Cycles,
            Samples = 128,
            ResolutionX = 512,
            ResolutionY = 512,
            FramesRendered = 3,
            RenderSeconds = 2.0
        };

        // 512 * 512 * 128 * 3 / 2.0 = 50,331,648
        var expected = 512d * 512d * 128d * 3d / 2.0;

        Assert.That(data.ComputeRate(), Is.EqualTo(expected).Within(1e-3));
    }

    [Test]
    public void ComputeRateScalesLinearlyWithResolutionTest()
    {
        // The v2 calibration relies on this: doubling each side (256→512) quadruples the rate for
        // the same frames/second, which is why v1 and v2 node rates are ~4× apart and must not mix.
        var small = NewData(resolution: 256);
        var large = NewData(resolution: 512);

        Assert.That(large.ComputeRate(), Is.EqualTo(small.ComputeRate() * 4.0).Within(1e-3));
    }

    [TestCase(0.0)]
    [TestCase(-1.0)]
    public void ComputeRateReturnsZeroForNonPositiveRenderSecondsTest(double renderSeconds)
    {
        var data = NewData(renderSeconds: renderSeconds);

        Assert.That(data.ComputeRate(), Is.EqualTo(0));
    }

    #endregion

    #region Tools

    private static RenderBenchmarkRunData NewData(
        int resolution = 512,
        int samples = 128,
        int framesRendered = 3,
        double renderSeconds = 2.0)
    {
        return new RenderBenchmarkRunData
        {
            Engine = RenderEngine.Cycles,
            Samples = samples,
            ResolutionX = resolution,
            ResolutionY = resolution,
            FramesRendered = framesRendered,
            RenderSeconds = renderSeconds
        };
    }

    #endregion
}
