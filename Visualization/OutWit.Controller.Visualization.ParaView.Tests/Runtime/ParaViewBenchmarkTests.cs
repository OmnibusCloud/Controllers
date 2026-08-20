using System.Globalization;
using System.Text.Json;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// The node benchmark against the fake pvpython: every iteration is a complete task cycle (a fresh
/// process per frame), so the cycle count maps onto MinDuration between MIN_CYCLES and MAX_CYCLES,
/// the rate is pixels per second of wall time, failure paths surface (triggered by markers in the
/// temp-storage path — the fake keys off its output_dir), cancellation kills a hung cycle, and the
/// workspace is cleaned up.
/// </summary>
[TestFixture]
public sealed class ParaViewBenchmarkTests
{
    #region Fields

    private string m_root = null!;

    private IWitTempStorage m_tempStorage = null!;

    private string m_fakePvpython = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var solutionRoot = ParaViewTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        m_fakePvpython = ParaViewTestPaths.FindFakePvpythonPath(solutionRoot) ?? string.Empty;
        if (m_fakePvpython.Length == 0)
            Assert.Ignore("fake-pvpython not built");
    }

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"pv_bench_{Guid.NewGuid():N}");
        m_tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "temp"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Measure Tests

    [Test]
    public async Task MeasureAsyncRunsTheMinimumCyclesWhenTheTargetIsTinyTest()
    {
        // A tiny MinDuration is satisfied after the first cycle, but one ~3 s sample is too noisy:
        // the floor is MIN_CYCLES.
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(1), WarmupIterations = 1 };

        var result = await ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, options, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Iterations, Is.EqualTo(ParaViewBenchmark.MIN_CYCLES));
            Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(result.Rate, Is.EqualTo(ParaViewBenchmark.MIN_CYCLES * (double)ParaViewBenchmark.CYCLE_WIDTH * ParaViewBenchmark.CYCLE_HEIGHT / result.Elapsed.TotalSeconds).Within(1.0));
            Assert.That(result.Unit, Is.EqualTo(ParaViewBenchmark.UNIT));
            Assert.That(result.DatasetId, Is.EqualTo(ParaViewBenchmark.DATASET_ID));
            Assert.That(result.Custom, Is.Not.Null);
            Assert.That(result.Custom![ParaViewBenchmark.CUSTOM_RENDER_WINDOW], Is.EqualTo("FakeOffscreenWindow"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_RENDER_DEVICE], Is.EqualTo("GPU"), "only vtkOSOpenGLRenderWindow is software");
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_RENDER_RESOLUTION], Is.EqualTo($"{ParaViewBenchmark.CYCLE_WIDTH}x{ParaViewBenchmark.CYCLE_HEIGHT}"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_CYCLES], Is.EqualTo(ParaViewBenchmark.MIN_CYCLES.ToString()));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_PARAVIEW_VERSION], Is.EqualTo("6.1.1-fake"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_SCENE_POINTS], Is.EqualTo("226981"));
            // Each fake cycle reports 20 ms of in-process render time; the sum shows the startup share.
            Assert.That(double.Parse(result.Custom[ParaViewBenchmark.CUSTOM_RENDER_SECONDS], CultureInfo.InvariantCulture),
                Is.EqualTo(ParaViewBenchmark.MIN_CYCLES * 0.02).Within(1e-6));
        });
    }

    [Test]
    public async Task MeasureAsyncStopsAtTheCycleCapWhenTheTargetIsLongTest()
    {
        // Fake cycles are ~0.1 s of process spawn; a 60 s target would run forever without the cap.
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromSeconds(60), WarmupIterations = 1 };

        var result = await ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, options, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Iterations, Is.EqualTo(ParaViewBenchmark.MAX_CYCLES));
            Assert.That(result.Rate, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task MeasureAsyncWithNullOptionsUsesTheEngineDefaultsTest()
    {
        var result = await ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, null, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Iterations, Is.InRange(ParaViewBenchmark.MIN_CYCLES, ParaViewBenchmark.MAX_CYCLES));
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.DatasetId, Is.EqualTo(ParaViewBenchmark.DATASET_ID));
        });
    }

    [Test]
    public async Task MeasureAsyncCleansUpItsWorkspaceTest()
    {
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(1), WarmupIterations = 1 };

        await ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, options, null, CancellationToken.None);

        var leftovers = Directory.Exists(m_tempStorage.RootPath)
            ? Directory.GetFiles(m_tempStorage.RootPath, "*", SearchOption.AllDirectories)
            : [];

        Assert.That(leftovers, Is.Empty, "the benchmark workspace must be removed after the run");
    }

    #endregion

    #region Failure Tests

    [Test]
    public void MeasureAsyncSurfacesARunnerFailureTest()
    {
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "fake-fail", "temp"));

        var error = Assert.ThrowsAsync<InvalidOperationException>(() =>
            ParaViewBenchmark.MeasureAsync(m_fakePvpython, tempStorage, null, null, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("exited with code 3"));
            Assert.That(error.Message, Does.Contain("stage=build"));
            Assert.That(error.Message, Does.Contain("fake benchmark failure requested"));
        });
    }

    [Test]
    public void MeasureAsyncRejectsAMissingStatusDocumentTest()
    {
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "fake-nostatus", "temp"));

        var error = Assert.ThrowsAsync<InvalidOperationException>(() =>
            ParaViewBenchmark.MeasureAsync(m_fakePvpython, tempStorage, null, null, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("wrote no status document"));
    }

    [Test]
    public void MeasureAsyncRejectsAMissingRuntimeTest()
    {
        var missing = Path.Combine(m_root, "no-such-pvpython" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

        var error = Assert.ThrowsAsync<InvalidOperationException>(() =>
            ParaViewBenchmark.MeasureAsync(missing, m_tempStorage, null, null, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("does not exist"));
    }

    [Test]
    public async Task CancellationKillsAHungBenchmarkCycleTest()
    {
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "fake-hang", "temp"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var started = DateTime.UtcNow;
        try
        {
            await ParaViewBenchmark.MeasureAsync(m_fakePvpython, tempStorage, null, null, cancellation.Token);
            Assert.Fail("a hung cycle must not complete the benchmark");
        }
        catch (OperationCanceledException)
        {
            // expected
        }

        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(30)), "cancellation must kill the runner promptly");
    }

    #endregion

    #region Contract Tests

    [Test]
    public void BenchmarkRunnerIsEmbeddedTest()
    {
        var text = ParaViewRuntimeInfo.ReadEmbeddedText(ParaViewBenchmark.RUNNER_RESOURCE);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.Not.Null);
            Assert.That(text, Does.Contain("def main("));
            Assert.That(text, Does.Contain("--task-file"));
            Assert.That(text, Does.Contain("os._exit(code)"), "the runner must exit hard: pvpython can hang in its finalizers");
            foreach (var key in new[] { "width", "height", "warmup_frames", "target_seconds", "max_frames", "extent", "output_dir", "status_path" })
                Assert.That(text, Does.Contain($"\"{key}\""), $"the runner must read '{key}' from the task document");
            foreach (var key in new[] { "frames", "render_seconds", "render_window", "paraview_version", "points" })
                Assert.That(text, Does.Contain($"\"{key}\""), $"the runner must report '{key}' in the status document");
        });
    }

    [Test]
    public void BuildTaskJsonRequestsExactlyOneFramePerCycleTest()
    {
        var json = ParaViewBenchmark.BuildTaskJson(@"C:\out dir\ünïcode", "/tmp/status.json");
        var document = JsonDocument.Parse(json).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(document.GetProperty("width").GetInt32(), Is.EqualTo(ParaViewBenchmark.CYCLE_WIDTH));
            Assert.That(document.GetProperty("height").GetInt32(), Is.EqualTo(ParaViewBenchmark.CYCLE_HEIGHT));
            Assert.That(document.GetProperty("warmup_frames").GetInt32(), Is.Zero, "warm-up is whole cycles in the controller, not frames in the runner");
            Assert.That(document.GetProperty("target_seconds").GetDouble(), Is.Zero, "the timed loop lives in the controller");
            Assert.That(document.GetProperty("max_frames").GetInt32(), Is.EqualTo(1), "one frame per process is the task shape");
            Assert.That(document.GetProperty("extent").GetInt32(), Is.EqualTo(ParaViewBenchmark.WAVELET_EXTENT));
            Assert.That(document.GetProperty("output_dir").GetString(), Is.EqualTo(@"C:\out dir\ünïcode"));
            Assert.That(document.GetProperty("status_path").GetString(), Is.EqualTo("/tmp/status.json"));
        });
    }

    [Test]
    public void BuildArgumentsPassesTheOffscreenOptionsThenTheRunnerThenTheTaskFileTest()
    {
        var arguments = ParaViewBenchmark.BuildArguments("/w/benchmark_frames.py", "/w/benchmark.json");

        Assert.That(arguments, Is.EqualTo(new[] { "--force-offscreen-rendering", "--disable-registry", "/w/benchmark_frames.py", "--task-file", "/w/benchmark.json" }));
    }

    [Test]
    public void RunDataTryReadParsesTheRunnerStatusAndToleratesGarbageTest()
    {
        var good = Path.Combine(m_root, "good.json");
        var bad = Path.Combine(m_root, "bad.json");
        Directory.CreateDirectory(m_root);
        File.WriteAllText(good, """{"schema":1,"ok":true,"stage":"done","error":"","frames":1,"render_seconds":0.7,"width":1920,"height":1080,"points":42,"render_window":"vtkOSOpenGLRenderWindow","paraview_version":"6.1.1","output_bytes":10}""");
        File.WriteAllText(bad, "{ not json");

        var parsed = ParaViewBenchmarkRunData.TryRead(good);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed!.Ok, Is.True);
            Assert.That(parsed.Frames, Is.EqualTo(1));
            Assert.That(parsed.RenderSeconds, Is.EqualTo(0.7).Within(1e-9));
            Assert.That(parsed.RenderWindow, Is.EqualTo("vtkOSOpenGLRenderWindow"));
            Assert.That(parsed.Points, Is.EqualTo(42));
            Assert.That(ParaViewBenchmarkRunData.TryRead(bad), Is.Null);
            Assert.That(ParaViewBenchmarkRunData.TryRead(Path.Combine(m_root, "absent.json")), Is.Null);
        });
    }

    [Test]
    public void ToResultComputesTheCycleRateAndMarksTheSoftwareWindowAsCpuTest()
    {
        var lastCycle = new ParaViewBenchmarkRunData { Ok = true, Frames = 1, RenderSeconds = 0.2, Width = 1920, Height = 1080, RenderWindow = "vtkOSOpenGLRenderWindow", ParaviewVersion = "6.1.1" };

        var result = ParaViewBenchmark.ToResult(lastCycle, cycles: 4, elapsed: TimeSpan.FromSeconds(12), renderSeconds: 0.8);

        Assert.Multiple(() =>
        {
            Assert.That(result.Rate, Is.EqualTo(4 * 1920.0 * 1080 / 12));
            Assert.That(result.Iterations, Is.EqualTo(4));
            Assert.That(result.Custom![ParaViewBenchmark.CUSTOM_RENDER_DEVICE], Is.EqualTo("CPU"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_CYCLES], Is.EqualTo("4"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_CYCLE_SECONDS], Is.EqualTo("3.000"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_RENDER_SECONDS], Is.EqualTo("0.800"));
        });
    }

    [Test]
    public void UnavailableResultHasZeroRateInTheBenchmarkUnitTest()
    {
        var result = ParaViewBenchmark.CreateUnavailableResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.Rate, Is.Zero);
            Assert.That(result.Iterations, Is.Zero);
            Assert.That(result.Unit, Is.EqualTo(ParaViewBenchmark.UNIT));
            Assert.That(result.DatasetId, Is.EqualTo(ParaViewBenchmark.DATASET_ID));
        });
    }

    #endregion
}
