using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Output;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Visualization.ParaView.Tests.Activities;

/// <summary>
/// The composed-scene scripts against a REAL pvpython of the pinned series: a bare corpus CalculiX
/// result (no state anywhere) is composed on the node by the real composer script — the bundled
/// reader opens it, the scene is coloured, fitted and saved by ParaView itself — validated by the
/// real host validator, split and rendered. The proof that the composer produces states the
/// allowlist accepts and that every timestep of the composed timeline renders. Ignored when no
/// runtime is available (OUTWIT_PVPYTHON or @Prerequisites/paraview).
/// </summary>
[TestFixture]
[Category("RealRuntime")]
public sealed class ParaViewComposeRealRuntimeTests
{
    #region Constants

    private const string TRANSIENT_FRD = "transient_heat.frd";

    private const string STATIC_FRD = "static.frd";

    #endregion

    #region Fields

    private string m_root = null!;

    private ParaViewTestBlobService m_blobs = null!;

    private IWitEngine m_engine = null!;

    private string m_scriptsPath = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var controllersPath = ParaViewTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_scriptsPath = ParaViewTestPaths.FindBundledScriptsPath() ?? string.Empty;
        if (m_scriptsPath.Length == 0)
            Assert.Ignore("@Scripts not found");

        if (!Directory.Exists(ParaViewCorpus.Root))
            Assert.Ignore("fixture corpus not present");

        var pvpython = ParaViewCorpus.FindPvpython();
        if (pvpython == null)
            Assert.Ignore("no ParaView runtime (set OUTWIT_PVPYTHON or place a distribution under @Prerequisites/paraview)");

        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, pvpython);

        m_root = Path.Combine(Path.GetTempPath(), $"pv_compose_real_{Guid.NewGuid():N}");
        m_blobs = new ParaViewTestBlobService(Path.Combine(m_root, "blobs"));

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobs));

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobs);
                services.AddSingleton<IWitNodesManager>(new ParaViewTestNodesManager(WitEngineNodeSdk.Instance));
            });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, null);

        if (m_root != null && Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task TransientResultComposesAndRendersEveryTimestepTest()
    {
        var data = DataScene(TRANSIENT_FRD);
        data.ColorArrayName = "NDTEMP";
        data.ColormapPreset = "Viridis";
        var options = new ParaViewOutputOptionsData { Width = 160, Height = 120, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

        var job = m_engine.Compile(Script("RenderParaViewDataFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, data, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");

        var scene = (job.Variables["scene"].Value as ParaViewSceneRefData)!;
        var report = (job.Variables["report"].Value as ParaViewValidationReportData)!;
        var result = (job.Variables["result"].Value as IReadOnlyList<Guid?>)!;

        Assert.Multiple(() =>
        {
            Assert.That(scene.TimestepValues, Is.EqualTo(new[] { 0.2, 0.4, 0.6, 0.8, 1.0 }).Within(1e-9), "the composed timeline is the reader's");
            Assert.That(scene.Runtime.ParaViewMajor, Is.EqualTo(ParaViewRuntimeInfo.RUNTIME_MAJOR));
            Assert.That(scene.Runtime.ParaViewMinor, Is.EqualTo(ParaViewRuntimeInfo.RUNTIME_MINOR));
            Assert.That(scene.Runtime.Plugins.Single().Version, Is.EqualTo(ParaViewRuntimeInfo.BundledReaderVersion()));
            Assert.That(report.IsValid, Is.True, string.Join("; ", report.Errors));
            Assert.That(report.Warnings, Is.Empty, string.Join("; ", report.Warnings));
            Assert.That(result, Has.Count.EqualTo(5));
        });

        foreach (var blobId in result)
            Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath(blobId!.Value)), Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 160, 120, false)));

        // The saved state is a real ParaView state on the data's logical path, carrying the colouring
        // the scene asked for and the reader as its only file reference.
        var stateText = await File.ReadAllTextAsync(m_blobs.GetStoredPath(scene.StateBlobId));
        Assert.Multiple(() =>
        {
            Assert.That(stateText, Does.Contain("<ServerManagerState"));
            Assert.That(stateText, Does.Contain($"data/{TRANSIENT_FRD}"));
            Assert.That(stateText, Does.Contain("OmnibusCloudFrdReader"));
            Assert.That(stateText, Does.Contain("NDTEMP"));
            Assert.That(stateText, Does.Not.Contain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)), "no node path survives in the state");
        });
        Assert.That(scene.PackageManifestJson, Does.Contain("\"colorArray\":\"NDTEMP\""));
    }

    [Test]
    public async Task StaticResultComposesAStillWithTheDefaultPresentationTest()
    {
        var data = DataScene(STATIC_FRD);
        var options = new ParaViewOutputOptionsData { Width = 320, Height = 240 };

        var job = m_engine.Compile(Script("RenderParaViewDataStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, data, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");

        var scene = (job.Variables["scene"].Value as ParaViewSceneRefData)!;
        var blobId = (Guid)job.Variables["result"].Value!;
        Assert.Multiple(() =>
        {
            Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath(blobId)), Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 320, 240, false)));
            Assert.That(scene.PackageManifestJson, Does.Contain("\"colorArray\":\""), "the first point array was chosen");
            Assert.That(scene.Attachments.Single().Sha256, Has.Length.EqualTo(64));
        });
    }

    [Test]
    public async Task MissingColorArrayFailsTheJobNamingTheArraysThatExistTest()
    {
        var data = DataScene(STATIC_FRD);
        data.ColorArrayName = "PRESSURE";

        var job = m_engine.Compile(Script("RenderParaViewDataStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, data, new ParaViewOutputOptionsData { Width = 64, Height = 48 });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("PRESSURE"));
    }

    [Test]
    public async Task ComposedStateWithATurntableRendersTheOrbitTest()
    {
        var data = DataScene(STATIC_FRD);
        data.CameraDirection = ParaViewCameraDirection.PlusZ;
        var options = new ParaViewOutputOptionsData
        {
            Width = 96,
            Height = 64,
            Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 0 },
            // A spiral with an approach: orbit + rise + dolly through the real runtime's camera.
            Turntable = new ParaViewTurntableData { Frames = 4, Degrees = 360, ElevationDegrees = 30.0, DollyFactor = 0.6 }
        };

        var job = m_engine.Compile(Script("RenderParaViewDataFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, data, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");
        var result = (job.Variables["result"].Value as IReadOnlyList<Guid?>)!;
        Assert.That(result, Has.Count.EqualTo(4));
    }

    [Test]
    public void EveryAllowlistedPresetExistsInTheRuntimeTest()
    {
        // The validator admits a preset name before any node runs; a name the runtime does not know
        // would only surface as a failed job. Ask the real runtime for its vocabulary.
        var pvpython = Environment.GetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH)!;
        var script = "from paraview import servermanager\n"
                     + "p = servermanager.vtkSMTransferFunctionPresets.GetInstance()\n"
                     + "print('\\n'.join('PRESET:' + p.GetPresetName(i) for i in range(p.GetNumberOfPresets())))\n";
        var scriptPath = Path.Combine(m_root, "presets.py");
        File.WriteAllText(scriptPath, script);

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pvpython)
        {
            ArgumentList = { "--force-offscreen-rendering", scriptPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var presets = output.Split('\n')
            .Select(me => me.Trim())
            .Where(me => me.StartsWith("PRESET:", StringComparison.Ordinal))
            .Select(me => me["PRESET:".Length..])
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(presets, Is.Not.Empty, "the runtime reported no presets");
        Assert.That(ParaViewDataSceneValidator.COLORMAP_PRESETS.Where(me => !presets.Contains(me)), Is.Empty,
            "every allowlisted colour-map preset must exist in the bundled runtime");
    }

    [Test]
    public async Task ComposeBenchmarkMeasuresARealCycleTest()
    {
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(1), WarmupIterations = 1 };

        var result = await WitEngineNodeSdk.Instance.RunBenchmark("ParaView.Compose", options);

        Assert.Multiple(() =>
        {
            Assert.That(result.Unit, Is.EqualTo(ParaViewComposeBenchmark.UNIT));
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.Iterations, Is.EqualTo(ParaViewComposeBenchmark.MIN_CYCLES));
            Assert.That(result.Custom?[ParaViewComposeBenchmark.CUSTOM_PARAVIEW_VERSION], Does.StartWith(ParaViewRuntimeInfo.RUNTIME_SERIES));
        });
    }

    #endregion

    #region Tools

    private ParaViewDataSceneData DataScene(string fileName)
    {
        var path = Path.Combine(ParaViewCorpus.Root, "data", "frd", fileName);
        Assert.That(File.Exists(path), Is.True, path);

        return new ParaViewDataSceneData
        {
            Attachments =
            [
                new ParaViewAttachmentRefData
                {
                    BlobId = m_blobs.RegisterExistingFile(path),
                    LogicalPath = $"data/{fileName}",
                    Role = ParaViewAttachmentRole.ReaderInput
                }
            ]
        };
    }

    private string Script(string fileName)
    {
        return File.ReadAllText(Path.Combine(m_scriptsPath, fileName));
    }

    #endregion
}
