using System.Text.Json;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// The node benchmark against the fake pvpython (deterministic 20 ms/frame): the rate is pixels per
/// second of the reported frames, options map onto the task document, the fallback target applies,
/// failure paths surface, cancellation kills a hung runner, and the workspace is cleaned up.
/// </summary>
[TestFixture]
public sealed class ParaViewBenchmarkTests
{
    #region Constants

    private const double FAKE_SECONDS_PER_FRAME = 0.02;

    private const int FAKE_MODE_FAIL = 1001;

    private const int FAKE_MODE_NO_STATUS = 1002;

    private const int FAKE_MODE_HANG = 1003;

    #endregion

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
    public async Task MeasureAsyncReportsPixelsPerSecondFromTheRunnerStatusTest()
    {
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromSeconds(1.5), WarmupIterations = 1 };

        var result = await ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, options, null, CancellationToken.None);

        var expectedFrames = (int)Math.Ceiling(1.5 / FAKE_SECONDS_PER_FRAME);
        var expectedSeconds = expectedFrames * FAKE_SECONDS_PER_FRAME;
        Assert.Multiple(() =>
        {
            Assert.That(result.Iterations, Is.EqualTo(expectedFrames));
            Assert.That(result.Elapsed.TotalSeconds, Is.EqualTo(expectedSeconds).Within(1e-6));
            Assert.That(result.Rate, Is.EqualTo(expectedFrames * (double)ParaViewBenchmark.RESOLUTION * ParaViewBenchmark.RESOLUTION / expectedSeconds).Within(1e-3));
            Assert.That(result.Unit, Is.EqualTo(ParaViewBenchmark.UNIT));
            Assert.That(result.DatasetId, Is.EqualTo(ParaViewBenchmark.DATASET_ID));
            Assert.That(result.Custom, Is.Not.Null);
            Assert.That(result.Custom![ParaViewBenchmark.CUSTOM_RENDER_WINDOW], Is.EqualTo("FakeOffscreenWindow"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_RENDER_DEVICE], Is.EqualTo("GPU"), "only vtkOSOpenGLRenderWindow is software");
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_RENDER_RESOLUTION], Is.EqualTo($"{ParaViewBenchmark.RESOLUTION}x{ParaViewBenchmark.RESOLUTION}"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_RENDER_FRAMES], Is.EqualTo(expectedFrames.ToString()));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_PARAVIEW_VERSION], Is.EqualTo("6.1.1-fake"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_SCENE_POINTS], Is.EqualTo("226981"));
        });
    }

    [Test]
    public async Task MeasureAsyncUsesTheFallbackTargetAndTheFrameCapWhenMinDurationIsZeroTest()
    {
        // No positive MinDuration → the 3 s fallback; at the fake's 20 ms/frame that asks for 150 frames,
        // which the frame cap trims to MAX_FRAMES.
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.Zero, WarmupIterations = 0 };

        var result = await ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, options, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Iterations, Is.EqualTo(ParaViewBenchmark.MAX_FRAMES));
            Assert.That(result.Elapsed.TotalSeconds, Is.EqualTo(ParaViewBenchmark.MAX_FRAMES * FAKE_SECONDS_PER_FRAME).Within(1e-6));
            Assert.That(result.Rate, Is.EqualTo((double)ParaViewBenchmark.RESOLUTION * ParaViewBenchmark.RESOLUTION / FAKE_SECONDS_PER_FRAME).Within(1e-3));
        });
    }

    [Test]
    public async Task MeasureAsyncWithNullOptionsUsesTheEngineDefaultsTest()
    {
        var result = await ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, null, null, CancellationToken.None);

        var expectedFrames = (int)Math.Ceiling(WitBenchmarkOptions.Default.MinDuration.TotalSeconds / FAKE_SECONDS_PER_FRAME);
        Assert.Multiple(() =>
        {
            Assert.That(result.Iterations, Is.EqualTo(expectedFrames));
            Assert.That(result.Rate, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task MeasureAsyncCleansUpItsWorkspaceTest()
    {
        await ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, null, null, CancellationToken.None);

        var leftovers = Directory.Exists(m_tempStorage.RootPath)
            ? Directory.GetFileSystemEntries(m_tempStorage.RootPath, "*", SearchOption.AllDirectories).Where(me => !Directory.Exists(me) || Directory.GetFileSystemEntries(me).Length > 0).ToList()
            : [];

        Assert.That(leftovers.Where(File.Exists), Is.Empty, "the benchmark workspace must be removed after the run");
    }

    #endregion

    #region Failure Tests

    [Test]
    public void MeasureAsyncSurfacesARunnerFailureTest()
    {
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromSeconds(1), WarmupIterations = FAKE_MODE_FAIL };

        var error = Assert.ThrowsAsync<InvalidOperationException>(() =>
            ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, options, null, CancellationToken.None));

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
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromSeconds(1), WarmupIterations = FAKE_MODE_NO_STATUS };

        var error = Assert.ThrowsAsync<InvalidOperationException>(() =>
            ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, options, null, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("wrote no status document"));
    }

    [Test]
    public void MeasureAsyncRejectsAMissingRuntimeTest()
    {
        var missing = Path.Combine(m_root, "no-such-pvpython" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            ParaViewBenchmark.MeasureAsync(missing, m_tempStorage, null, null, CancellationToken.None));
    }

    [Test]
    public async Task CancellationKillsAHungBenchmarkRunnerTest()
    {
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromSeconds(1), WarmupIterations = FAKE_MODE_HANG };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var started = DateTime.UtcNow;
        try
        {
            await ParaViewBenchmark.MeasureAsync(m_fakePvpython, m_tempStorage, options, null, cancellation.Token);
            Assert.Fail("a hung runner must not complete the benchmark");
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
    public void BuildTaskJsonCarriesTheBenchmarkContractTest()
    {
        var json = ParaViewBenchmark.BuildTaskJson(@"C:\out dir\ünïcode", "/tmp/status.json", 2, TimeSpan.FromSeconds(2.5));
        var document = JsonDocument.Parse(json).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(document.GetProperty("width").GetInt32(), Is.EqualTo(ParaViewBenchmark.RESOLUTION));
            Assert.That(document.GetProperty("height").GetInt32(), Is.EqualTo(ParaViewBenchmark.RESOLUTION));
            Assert.That(document.GetProperty("warmup_frames").GetInt32(), Is.EqualTo(2));
            Assert.That(document.GetProperty("target_seconds").GetDouble(), Is.EqualTo(2.5).Within(1e-9));
            Assert.That(document.GetProperty("max_frames").GetInt32(), Is.EqualTo(ParaViewBenchmark.MAX_FRAMES));
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
    public void RunDataComputesPixelsPerSecondAndZeroWhenNothingWasMeasuredTest()
    {
        var measured = new ParaViewBenchmarkRunData { Frames = 10, RenderSeconds = 2, Width = 100, Height = 50 };
        var empty = new ParaViewBenchmarkRunData { Frames = 0, RenderSeconds = 0, Width = 100, Height = 50 };
        var instantaneous = new ParaViewBenchmarkRunData { Frames = 3, RenderSeconds = 0, Width = 100, Height = 50 };

        Assert.Multiple(() =>
        {
            Assert.That(measured.ComputeRate(), Is.EqualTo(10 * 100 * 50 / 2.0));
            Assert.That(empty.ComputeRate(), Is.Zero);
            Assert.That(instantaneous.ComputeRate(), Is.Zero);
        });
    }

    [Test]
    public void RunDataTryReadParsesTheRunnerStatusAndToleratesGarbageTest()
    {
        var good = Path.Combine(m_root, "good.json");
        var bad = Path.Combine(m_root, "bad.json");
        Directory.CreateDirectory(m_root);
        File.WriteAllText(good, """{"schema":1,"ok":true,"stage":"done","error":"","frames":7,"render_seconds":0.7,"width":512,"height":512,"points":42,"render_window":"vtkOSOpenGLRenderWindow","paraview_version":"6.1.1","output_bytes":10}""");
        File.WriteAllText(bad, "{ not json");

        var parsed = ParaViewBenchmarkRunData.TryRead(good);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed!.Ok, Is.True);
            Assert.That(parsed.Frames, Is.EqualTo(7));
            Assert.That(parsed.RenderSeconds, Is.EqualTo(0.7).Within(1e-9));
            Assert.That(parsed.RenderWindow, Is.EqualTo("vtkOSOpenGLRenderWindow"));
            Assert.That(parsed.Points, Is.EqualTo(42));
            Assert.That(ParaViewBenchmarkRunData.TryRead(bad), Is.Null);
            Assert.That(ParaViewBenchmarkRunData.TryRead(Path.Combine(m_root, "absent.json")), Is.Null);
        });
    }

    [Test]
    public void ToResultMarksTheSoftwareWindowAsCpuTest()
    {
        var data = new ParaViewBenchmarkRunData { Ok = true, Frames = 12, RenderSeconds = 3, Width = 512, Height = 512, RenderWindow = "vtkOSOpenGLRenderWindow", ParaviewVersion = "6.1.1" };

        var result = ParaViewBenchmark.ToResult(data);

        Assert.Multiple(() =>
        {
            Assert.That(result.Rate, Is.EqualTo(12 * 512.0 * 512 / 3));
            Assert.That(result.Custom![ParaViewBenchmark.CUSTOM_RENDER_DEVICE], Is.EqualTo("CPU"));
            Assert.That(result.Custom[ParaViewBenchmark.CUSTOM_RENDER_SECONDS], Is.EqualTo("3.000"));
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
