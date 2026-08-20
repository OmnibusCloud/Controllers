using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// The GPU/EGL backend decision: renderer classification (an EGL window over llvmpipe is still
/// software), the probe protocol against the fake pvpython, the operations override, and the
/// platform gate (only headless Linux overrides the window class; the full Linux decision path is
/// exercised by the docker certification, not here).
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ParaViewRenderingBackendTests
{
    #region Fields

    private string m_root = null!;

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
        m_root = Path.Combine(Path.GetTempPath(), $"pv_backend_{Guid.NewGuid():N}");
        ParaViewRenderingBackend.ResetCache();
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(ParaViewRenderingBackend.ENV_OPENGL_WINDOW, null);
        ParaViewRenderingBackend.ResetCache();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Classification Tests

    [TestCase("NVIDIA GeForce GTX 1080 Ti/PCIe/SSE2", true)]
    [TestCase("AMD Radeon RX 7900 XTX (radeonsi)", true)]
    [TestCase("Mesa Intel(R) UHD Graphics 770", true)]
    [TestCase("llvmpipe (LLVM 15.0.7, 256 bits)", false)]
    [TestCase("softpipe", false)]
    [TestCase("Mesa OffScreen", false)]
    [TestCase("swrast (software rasterizer)", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void HardwareRendererClassificationTest(string? renderer, bool expected)
    {
        Assert.That(ParaViewRenderingBackend.IsHardwareRenderer(renderer), Is.EqualTo(expected));
    }

    #endregion

    #region Probe Tests

    [Test]
    public async Task ProbeReportsTheRequestedWindowAndTheRendererTest()
    {
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "fake-egl-hw", "temp"));

        var status = await ParaViewRenderingBackend.ProbeAsync(
            m_fakePvpython, tempStorage, ParaViewRenderingBackend.EGL_WINDOW, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.Not.Null);
            Assert.That(status!.Ok, Is.True);
            Assert.That(status.RenderWindow, Is.EqualTo(ParaViewRenderingBackend.EGL_WINDOW), "the fake echoes VTK_DEFAULT_OPENGL_WINDOW");
            Assert.That(ParaViewRenderingBackend.IsHardwareRenderer(status.Renderer), Is.True);
        });
    }

    [Test]
    public async Task ProbeSeesThroughAnEglWindowOverASoftwareRasterizerTest()
    {
        // Mesa's EGL happily reports vtkEGLRenderWindow while rasterizing on llvmpipe; the window
        // class alone must never be enough to claim a GPU.
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "fake-egl-sw", "temp"));

        var status = await ParaViewRenderingBackend.ProbeAsync(
            m_fakePvpython, tempStorage, ParaViewRenderingBackend.EGL_WINDOW, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.Not.Null);
            Assert.That(status!.RenderWindow, Is.EqualTo(ParaViewRenderingBackend.EGL_WINDOW));
            Assert.That(ParaViewRenderingBackend.IsHardwareRenderer(status.Renderer), Is.False, "llvmpipe behind EGL is software");
        });
    }

    [Test]
    public async Task ProbeCrashYieldsNoStatusTest()
    {
        // An unusable window class typically crashes pvpython outright; the caller must get null,
        // never an exception, so the resolution can fall back to OSMesa.
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "fake-probe-crash", "temp"));

        var status = await ParaViewRenderingBackend.ProbeAsync(
            m_fakePvpython, tempStorage, ParaViewRenderingBackend.EGL_WINDOW, null, CancellationToken.None);

        Assert.That(status, Is.Null);
    }

    #endregion

    #region Demotion Tests

    [Test]
    public async Task TaskFailingOnEglDemotesTheNodeAndRetriesOnSoftwareTest()
    {
        // The production incident, replayed: EGL passes the probe (here: forced via the override),
        // then the real task's runner dies like a segfault. One local retry on OSMesa must succeed
        // and the job must never see the crash.
        Environment.SetEnvironmentVariable(ParaViewRenderingBackend.ENV_OPENGL_WINDOW, ParaViewRenderingBackend.EGL_WINDOW);
        var blobs = new ParaViewTestBlobService(Path.Combine(m_root, "blobs"));
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "temp"));
        var executor = new ParaViewTaskExecutor(blobs, tempStorage, ParaViewProxyAllowlist.LoadEmbedded(ParaViewRuntimeInfo.RUNTIME_SERIES), NullLogger.Instance);

        var state = ParaViewStateBuilder.Typical("data/field_0.vtu").WithExtraStateContent("<!-- FAKE-EGL-CRASH -->").Build();
        var task = BuildTask(blobs, state);

        var result = await executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Width, Is.EqualTo(64), "the OSMesa retry must produce the real result");
            Assert.That(result.Height, Is.EqualTo(48));
        });
    }

    [Test]
    public async Task BenchmarkFailingOnEglDemotesAndMeasuresOnSoftwareTest()
    {
        Environment.SetEnvironmentVariable(ParaViewRenderingBackend.ENV_OPENGL_WINDOW, ParaViewRenderingBackend.EGL_WINDOW);
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "fake-egl-crash", "temp"));
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(1), WarmupIterations = 1 };

        var result = await ParaViewBenchmark.MeasureAsync(m_fakePvpython, tempStorage, options, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Rate, Is.GreaterThan(0), "the OSMesa re-measure must yield a real rate");
            Assert.That(result.Iterations, Is.EqualTo(ParaViewBenchmark.MIN_CYCLES));
        });
    }

    [Test]
    public void NonEglFailuresAreNotSwallowedByTheRetryTest()
    {
        // A genuine scene failure on the software path must still surface as an error - the retry
        // exists only for the EGL edge.
        Environment.SetEnvironmentVariable(ParaViewRenderingBackend.ENV_OPENGL_WINDOW, ParaViewRunnerEnvironment.OSMESA_WINDOW);
        var blobs = new ParaViewTestBlobService(Path.Combine(m_root, "blobs"));
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "temp"));
        var executor = new ParaViewTaskExecutor(blobs, tempStorage, ParaViewProxyAllowlist.LoadEmbedded(ParaViewRuntimeInfo.RUNTIME_SERIES), NullLogger.Instance);

        var state = ParaViewStateBuilder.Typical("data/field_0.vtu").WithExtraStateContent("<!-- FAKE-FAIL -->").Build();
        var task = BuildTask(blobs, state);

        Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None));
    }

    #endregion

    #region Tools

    private ParaViewRenderTaskData BuildTask(ParaViewTestBlobService blobs, string stateXml)
    {
        var package = new ParaViewPackageBuilder(Path.Combine(m_root, "pkg_" + Guid.NewGuid().ToString("N")), blobs)
            .AddFile("data/field_0.vtu", "field 0", seriesGroup: "field", timestepIndices: [0]);
        var scene = package.BuildScene(stateXml, timestepValues: [0]);
        var options = new ParaViewOutputOptionsData { ViewId = "RenderView1", Width = 64, Height = 48 };
        var report = new ParaViewValidationReportData
        {
            IsValid = true,
            ResolvedViewId = "RenderView1",
            ResolvedTimestepIndices = [0],
            TimestepValues = [0],
            PackageDigest = ParaViewPackageDigest.ComputePackageDigest(scene)
        };

        return ParaViewTaskSplitter.Split(scene, report, options).Single();
    }

    #endregion

    #region Resolution Tests

    [Test]
    public async Task EnvironmentOverridePinsTheWindowWithoutProbingTest()
    {
        Environment.SetEnvironmentVariable(ParaViewRenderingBackend.ENV_OPENGL_WINDOW, ParaViewRunnerEnvironment.OSMESA_WINDOW);

        // A nonexistent pvpython proves no probe process is ever launched on this path.
        var resolved = await ParaViewRenderingBackend.ResolveWindowAsync(
            Path.Combine(m_root, "no-such-pvpython.exe"), new WitTempStorageDefault(Path.Combine(m_root, "temp")), null, CancellationToken.None);

        Assert.That(resolved, Is.EqualTo(ParaViewRunnerEnvironment.OSMESA_WINDOW));
    }

    [Test]
    public async Task NonLinuxPlatformsUseThePlatformDefaultTest()
    {
        if (OperatingSystem.IsLinux())
            Assert.Ignore("this asserts the non-Linux gate; the Linux decision path is certified in docker");

        var resolved = await ParaViewRenderingBackend.ResolveWindowAsync(
            m_fakePvpython, new WitTempStorageDefault(Path.Combine(m_root, "temp")), null, CancellationToken.None);

        Assert.That(resolved, Is.Null, "Windows/macOS render through pvpython's own default (hardware GL where a driver exists)");
    }

    #endregion
}
