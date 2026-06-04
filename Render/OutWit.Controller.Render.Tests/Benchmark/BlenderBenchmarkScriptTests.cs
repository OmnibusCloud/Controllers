using System.Globalization;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Benchmark;

/// <summary>
/// Unit tests for <see cref="BlenderBenchmarkScript"/> — the pure Python-script generator and
/// marker parser for the node render benchmark. No Blender process is launched; these assert the
/// generated script is faithful to the requested parameters (resolution / grid / samples / warmup /
/// adaptive loop) and that the result parser is strict about its timing markers.
/// </summary>
[TestFixture]
public sealed class BlenderBenchmarkScriptTests
{
    #region Build Tests

    [Test]
    public void BuildScriptEmitsRequestedResolutionTest()
    {
        var script = string.Join("\n", BuildSample(resolution: 512));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("scene.render.resolution_x = 512"));
            Assert.That(script, Does.Contain("scene.render.resolution_y = 512"));
            Assert.That(script, Does.Contain("scene.render.resolution_percentage = 100"));
        });
    }

    [Test]
    public void BuildScriptEmitsRequestedSceneGridTest()
    {
        var script = string.Join("\n", BuildSample(gridSize: 4));

        Assert.That(script, Does.Contain("_grid = 4"));
    }

    [Test]
    public void BuildScriptEmitsRequestedWarmupFrameCountTest()
    {
        var script = string.Join("\n", BuildSample(warmupFrames: 1));

        // Warmup loop precedes the timed loop and renders without timing.
        Assert.That(script, Does.Contain("for _w in range(1):"));
    }

    [Test]
    public void BuildScriptEmitsAdaptiveTimedLoopWithTargetAndCapTest()
    {
        var script = string.Join("\n", BuildSample(targetSeconds: 3.0, maxFrames: 24));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("_target = 3"));
            Assert.That(script, Does.Contain("_maxf = 24"));
            Assert.That(script, Does.Contain("while _f < _maxf:"));
            Assert.That(script, Does.Contain("if time.perf_counter() - _t0 >= _target:"));
            // Only the render loop is timed (perf_counter started right before it).
            Assert.That(script, Does.Contain("_t0 = time.perf_counter()"));
        });
    }

    [Test]
    public void BuildScriptEmitsBothTimingMarkersTest()
    {
        var script = string.Join("\n", BuildSample());

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain(BlenderBenchmarkScript.FRAMES_MARKER));
            Assert.That(script, Does.Contain(BlenderBenchmarkScript.RENDER_SECONDS_MARKER));
        });
    }

    [TestCase(RenderEngine.Cycles, 8)]
    [TestCase(RenderEngine.Eevee, 8)]
    [TestCase(RenderEngine.GreasePencil, 8)]
    public void BuildScriptOnlyEmitsCyclesMaxBouncesForCyclesTest(RenderEngine engine, int maxBounces)
    {
        var script = string.Join("\n", BuildSample(engine: engine, maxBounces: maxBounces));

        // max_bounces is a Cycles-only light-path setting; Eevee / Grease Pencil ignore it.
        if (engine == RenderEngine.Cycles)
            Assert.That(script, Does.Contain($"scene.cycles.max_bounces = {maxBounces}"));
        else
            Assert.That(script, Does.Not.Contain("scene.cycles.max_bounces"));
    }

    #endregion

    #region Parse Tests

    [Test]
    public void ParseResultReadsFramesAndSecondsFromMarkersTest()
    {
        var stdout =
            $"noise line\n{BlenderBenchmarkScript.FRAMES_MARKER}7\n" +
            $"{BlenderBenchmarkScript.RENDER_SECONDS_MARKER}2.500000\ntrailing";

        var result = BlenderBenchmarkScript.ParseResult(stdout);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Value.Frames, Is.EqualTo(7));
            Assert.That(result.Value.RenderSeconds, Is.EqualTo(2.5).Within(1e-9));
        });
    }

    [Test]
    public void ParseResultIsInvariantCultureForSecondsTest()
    {
        // A comma-decimal stdout must NOT parse as 2500 under a comma-decimal current culture.
        var stdout =
            $"{BlenderBenchmarkScript.FRAMES_MARKER}3\n" +
            $"{BlenderBenchmarkScript.RENDER_SECONDS_MARKER}1.250000";

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = BlenderBenchmarkScript.ParseResult(stdout);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.RenderSeconds, Is.EqualTo(1.25).Within(1e-9));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestCase("only frames", "OUTWIT_BENCH_FRAMES=5")]
    [TestCase("only seconds", "OUTWIT_BENCH_RENDER_SECONDS=1.0")]
    [TestCase("neither", "unrelated output")]
    public void ParseResultReturnsNullWhenAMarkerIsMissingTest(string _, string stdout)
    {
        Assert.That(BlenderBenchmarkScript.ParseResult(stdout), Is.Null);
    }

    [TestCase("OUTWIT_BENCH_FRAMES=notnumber\nOUTWIT_BENCH_RENDER_SECONDS=1.0")]
    [TestCase("OUTWIT_BENCH_FRAMES=3\nOUTWIT_BENCH_RENDER_SECONDS=notnumber")]
    [TestCase("OUTWIT_BENCH_FRAMES=0\nOUTWIT_BENCH_RENDER_SECONDS=1.0")]
    [TestCase("OUTWIT_BENCH_FRAMES=3\nOUTWIT_BENCH_RENDER_SECONDS=0")]
    [TestCase("OUTWIT_BENCH_FRAMES=-1\nOUTWIT_BENCH_RENDER_SECONDS=1.0")]
    public void ParseResultReturnsNullForInvalidOrNonPositiveValuesTest(string stdout)
    {
        Assert.That(BlenderBenchmarkScript.ParseResult(stdout), Is.Null);
    }

    #endregion

    #region Tools

    private static IReadOnlyList<string> BuildSample(
        RenderEngine engine = RenderEngine.Cycles,
        int samples = 128,
        int resolution = 512,
        int gridSize = 4,
        int maxBounces = 8,
        int warmupFrames = 1,
        double targetSeconds = 3.0,
        int maxFrames = 24)
    {
        return BlenderBenchmarkScript.BuildScript(
            engine, samples, resolution, gridSize, maxBounces, warmupFrames, targetSeconds, maxFrames);
    }

    #endregion
}
