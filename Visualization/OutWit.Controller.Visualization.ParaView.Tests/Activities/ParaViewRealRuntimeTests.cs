using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Output;
using OutWit.Controller.Visualization.ParaView.Processes;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Visualization.ParaView.Tests.Activities;

/// <summary>
/// The runtime proof (docs 03, milestone M1/M2): the bundled scripts run through host + worker-node
/// engines against a REAL pvpython of the pinned series over the golden corpus — a still, every frame
/// of the PVD time series (each task materializing only index + anchor + its own piece), the
/// file-series reader, volume rendering — with real PNGs validated by the adapter. Skipped when no
/// runtime is available (OUTWIT_PVPYTHON or @Prerequisites/paraview).
/// </summary>
[TestFixture]
[Category("RealRuntime")]
public sealed class ParaViewRealRuntimeTests
{
    #region Fields

    private string m_root = null!;

    private string m_pvpython = null!;

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

        // A consumer's module cannot be exercised from this process: the test project references the
        // controller, so its bin copy shadows the module's assembly (Assembly.Location, which the runtime
        // resolver reads, would be this folder). RuntimeTools/verify_consumer.sh runs the Tests.ConsumerRunner
        // — a node-like process without controller references — for that.
        var pvpython = ParaViewCorpus.FindPvpython();
        if (pvpython == null)
            Assert.Ignore("no ParaView runtime (set OUTWIT_PVPYTHON or place a distribution under @Prerequisites/paraview)");

        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, pvpython);

        m_pvpython = pvpython!;

        m_root = Path.Combine(Path.GetTempPath(), $"pv_real_{Guid.NewGuid():N}");
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

    [TestCase(ParaViewCorpus.VTI_CONTOUR)]
    [TestCase(ParaViewCorpus.VTI_VOLUME)]
    [TestCase(ParaViewCorpus.VTU_SLICE_CLIP_GLYPH)]
    [TestCase(ParaViewCorpus.VTR_SURFACE)]
    [TestCase(ParaViewCorpus.SPHERE_STATIC)]
    public async Task StillRendersThroughTheRealRuntimeTest(string stateName)
    {
        var (scene, _) = ParaViewCorpus.BuildScene(stateName, Path.Combine(m_root, Path.GetFileNameWithoutExtension(stateName)), m_blobs);
        var options = new ParaViewOutputOptionsData { Width = 320, Height = 240 };

        var job = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");

        var blobId = (Guid)job.Variables["result"].Value!;
        var image = ParaViewImageInfo.TryRead(m_blobs.GetStoredPath(blobId));
        Assert.That(image, Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 320, 240, false)));

        var rendered = (job.Variables["rendered"].Value as IReadOnlyList<ParaViewRenderResultData?>)!.Single()!;
        Assert.That(rendered.RuntimeVersion, Does.StartWith(ParaViewRuntimeInfo.RUNTIME_SERIES));
        Assert.That(rendered.Diagnostics, Does.Contain("stage=done"));
    }

    [Test]
    public async Task PvdSeriesRendersEveryFrameWithSubsetOnlyDownloadsTest()
    {
        var (scene, package) = ParaViewCorpus.BuildScene(ParaViewCorpus.PVD_SERIES, Path.Combine(m_root, "pvd"), m_blobs);
        var options = new ParaViewOutputOptionsData { Width = 160, Height = 120, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

        m_blobs.ClearRequests();
        var job = m_engine.Compile(Script("RenderParaViewFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");

        var result = (job.Variables["result"].Value as IReadOnlyList<Guid?>)!;
        Assert.That(result, Has.Count.EqualTo(5));
        foreach (var blobId in result)
            Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath(blobId!.Value)), Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 160, 120, false)));

        // The corpus contours a wavelet whose amplitude grows per step, so every frame must differ:
        // a task that silently rendered its anchor piece (series_000) instead of its own would
        // reproduce frame 0 and be caught here.
        var digests = result.Select(me => Digest(m_blobs.GetStoredPath(me!.Value))).ToList();
        Assert.That(digests.Distinct().Count(), Is.EqualTo(5), "frames of different timesteps must differ");

        // Every task downloaded the index, the anchor (piece 0) and its own piece — nothing else.
        var requests = m_blobs.Requests.GroupBy(me => me).ToDictionary(me => me.Key, me => me.Count());
        Assert.Multiple(() =>
        {
            Assert.That(requests[package.BlobOf("data/series/series.pvd")], Is.EqualTo(5));
            Assert.That(requests[package.BlobOf("data/series/series_000.vti")], Is.EqualTo(5));
            for (var i = 1; i < 5; i++)
                Assert.That(requests[package.BlobOf($"data/series/series_{i:D3}.vti")], Is.EqualTo(1), $"piece {i}");
            Assert.That(requests[scene.StateBlobId], Is.EqualTo(6));
        });

        var rendered = (job.Variables["rendered"].Value as IReadOnlyList<ParaViewRenderResultData?>)!.Select(me => me!).OrderBy(me => me.TaskIndex).ToList();
        Assert.That(rendered.Select(me => me.TimeValue), Is.EqualTo(new double?[] { 0.0, 0.5, 1.0, 1.5, 2.0 }));
    }

    [Test]
    public async Task FileSeriesRendersAMiddleFrameWithAnchorAndOwnPieceTest()
    {
        var (scene, package) = ParaViewCorpus.BuildScene(ParaViewCorpus.FILE_SERIES, Path.Combine(m_root, "files"), m_blobs);
        var options = new ParaViewOutputOptionsData { Width = 160, Height = 120, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 3 } };

        m_blobs.ClearRequests();
        var job = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");

        var requested = m_blobs.Requests.Distinct().ToList();
        Assert.That(requested, Is.EquivalentTo(new[] { scene.StateBlobId, package.BlobOf("data/series/series_000.vti"), package.BlobOf("data/series/series_003.vti") }));

        // Frame 3 must not be the anchor's frame: render the first step too and compare.
        var first = new ParaViewOutputOptionsData { Width = 160, Height = 120, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 0 } };
        var firstJob = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var firstStatus = await m_engine.ScheduleAndWaitAsync(firstJob, scene, first);

        Assert.That(firstStatus.Result, Is.EqualTo(WitProcessingResult.Completed), $"{firstStatus.Result}: {firstStatus.Message}");
        Assert.That(
            Digest(m_blobs.GetStoredPath((Guid)job.Variables["result"].Value!)),
            Is.Not.EqualTo(Digest(m_blobs.GetStoredPath((Guid)firstJob.Variables["result"].Value!))),
            "the middle frame must differ from the anchor's frame");
    }

    [TestCase(ParaViewCorpus.GUI_NATIVE)]
    [TestCase(ParaViewCorpus.GUI_FILTERS)]
    [TestCase(ParaViewCorpus.GUI_CHART)]
    [TestCase(ParaViewCorpus.GUI_FOLDER + "/" + ParaViewCorpus.VTU_SLICE_CLIP_GLYPH)]
    public async Task GuiSavedStateRendersThroughTheRealRuntimeTest(string stateName)
    {
        // A state the ParaView GUI saved (root <ParaView>, legends, annotations, a chart view next to
        // the render view) must load and render through the runner exactly like the pvpython twins.
        var (scene, _) = ParaViewCorpus.BuildScene(stateName, Path.Combine(m_root, Path.GetFileNameWithoutExtension(stateName) + "_gui"), m_blobs);
        var options = new ParaViewOutputOptionsData { Width = 320, Height = 240 };

        var job = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");
        Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath((Guid)job.Variables["result"].Value!)), Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 320, 240, false)));
    }

    [TestCase(ParaViewCorpus.FRD_STATIC)]
    [TestCase(ParaViewCorpus.FRD_QUADRATIC)]
    public async Task FrdStillRendersThroughTheBundledReaderTest(string stateName)
    {
        var (scene, _) = ParaViewCorpus.BuildScene(stateName, Path.Combine(m_root, Path.GetFileNameWithoutExtension(stateName)), m_blobs);
        var options = new ParaViewOutputOptionsData { Width = 320, Height = 240 };

        var job = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");

        var image = ParaViewImageInfo.TryRead(m_blobs.GetStoredPath((Guid)job.Variables["result"].Value!));
        Assert.That(image, Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 320, 240, false)));

        var rendered = (job.Variables["rendered"].Value as IReadOnlyList<ParaViewRenderResultData?>)!.Single()!;
        Assert.That(rendered.ReaderVersion, Is.EqualTo(ParaViewRuntimeInfo.BundledReaderVersion()), "the runner loaded the bundled reader");
    }

    [TestCase(ParaViewCorpus.FRD_TRANSIENT, 5)]
    [TestCase(ParaViewCorpus.FRD_MODES, 4)]
    public async Task FrdTimeStepsRenderDistinctFramesTest(string stateName, int frameCount)
    {
        var (scene, _) = ParaViewCorpus.BuildScene(stateName, Path.Combine(m_root, Path.GetFileNameWithoutExtension(stateName)), m_blobs);
        var options = new ParaViewOutputOptionsData { Width = 160, Height = 120, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

        var job = m_engine.Compile(Script("RenderParaViewFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");

        var result = (job.Variables["result"].Value as IReadOnlyList<Guid?>)!;
        Assert.That(result, Has.Count.EqualTo(frameCount));
        var digests = result.Select(me => Digest(m_blobs.GetStoredPath(me!.Value))).ToList();
        Assert.That(digests.Distinct().Count(), Is.EqualTo(frameCount), "every step of the reader's time series must render differently");

        var rendered = (job.Variables["rendered"].Value as IReadOnlyList<ParaViewRenderResultData?>)!.Select(me => me!).OrderBy(me => me.TaskIndex).ToList();
        Assert.That(rendered.Select(me => me.TimeValue!.Value), Is.EqualTo(ParaViewCorpus.TimelineOf(stateName)).Within(1e-9));
    }

    [Test]
    public void FrdReaderElementMappingIsProvenOnEveryCgxTypeTest()
    {
        // RuntimeTools/check_frd_reader.py under the real pvpython: every cgx element type ccx writes
        // (he8, pe6, tet4, he20, pe15, tet10, tr6, qu8, be3) maps to a valid, non-inverted VTK cell
        // whose mid-side nodes are its edge midpoints, every result array has its shape, every time
        // step has data. The script is the author-side proof; this test keeps it in the suite.
        var pvpython = m_pvpython;
        var tools = ParaViewTestPaths.FindRuntimeToolsPath();
        if (tools == null)
            Assert.Ignore("RuntimeTools not found (tests run outside the repository)");

        var plugin = Path.Combine(Path.GetTempPath(), $"pv_reader_{Guid.NewGuid():N}.py");
        File.WriteAllText(plugin, ParaViewRuntimeInfo.ReadEmbeddedText(ParaViewRuntimeInfo.FRD_READER_RESOURCE)!);
        try
        {
            var arguments = new List<string>
            {
                "--force-offscreen-rendering", "--disable-registry", Path.Combine(tools, "check_frd_reader.py"), "--plugin", plugin,
                "--expect", "he20_c3d20.frd=vtkQuadraticHexahedron,1", "--expect", "pe15_c3d15.frd=vtkQuadraticWedge,1",
                "--expect", "tet10_c3d10.frd=vtkQuadraticTetra,1", "--expect", "pe6_c3d6.frd=vtkWedge,1", "--expect", "tet4_c3d4.frd=vtkTetra,1",
                "--expect", "shell_s8_3d.frd=vtkQuadraticHexahedron,1", "--expect", "shell_s8_2d.frd=vtkQuadraticQuad,1",
                "--expect", "shell_s6_3d.frd=vtkQuadraticWedge,1", "--expect", "shell_s6_2d.frd=vtkQuadraticTriangle,1",
                "--expect", "beam_b32_3d.frd=vtkQuadraticHexahedron,1", "--expect", "beam_b32_2d.frd=vtkQuadraticEdge,1",
                "--expect", "static.frd=vtkHexahedron,2", "--expect", "heat.frd=vtkHexahedron,125", "--expect", "transient_heat.frd=vtkHexahedron,2",
            };
            arguments.AddRange(ParaViewCorpus.FrdFiles.Select(me => Path.Combine(ParaViewCorpus.Root, me.Replace('/', Path.DirectorySeparatorChar))));

            var start = new System.Diagnostics.ProcessStartInfo(pvpython) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            var errors = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.That(process.ExitCode, Is.EqualTo(0), output + errors);
            Assert.That(output, Does.Contain($"checked {ParaViewCorpus.FrdFiles.Count} file(s), 0 failed"));
        }
        finally
        {
            File.Delete(plugin);
        }
    }

    [Test]
    public async Task JpegStillRendersThroughTheRealRuntimeTest()
    {
        var (scene, _) = ParaViewCorpus.BuildScene(ParaViewCorpus.VTI_CONTOUR, Path.Combine(m_root, "jpeg"), m_blobs);
        var options = new ParaViewOutputOptionsData { Width = 200, Height = 150, Format = ParaViewImageFormat.Jpeg };

        var job = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");
        Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath((Guid)job.Variables["result"].Value!)), Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Jpeg, 200, 150, false)));
    }

    [Test]
    public async Task UnicodeAndSpacesInLogicalPathsRenderTest()
    {
        // Clients name their files in their own language: the same scene with its data under a
        // logical path carrying Cyrillic letters and a space must render through the real runtime
        // (task.json, the state rewrite and the reader's file open all travel through UTF-8).
        var logicalPath = "data/результаты расчёта/волна.vti";
        var package = new ParaViewPackageBuilder(Path.Combine(m_root, "unicode"), m_blobs);
        package.AddFile(logicalPath, File.ReadAllBytes(Path.Combine(ParaViewCorpus.Root, "data", "wavelet.vti")), ParaViewAttachmentRole.ReaderInput, "", null, 0);
        var stateXml = File.ReadAllText(ParaViewCorpus.StatePath(ParaViewCorpus.VTI_CONTOUR)).Replace("data/wavelet.vti", logicalPath);
        var scene = package.BuildScene(stateXml);
        var options = new ParaViewOutputOptionsData { Width = 160, Height = 120 };

        var job = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");
        Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath((Guid)job.Variables["result"].Value!)), Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 160, 120, false)));

        // ... and it must be the contour scene, not an empty view: same image as the ASCII twin.
        var (twin, _) = ParaViewCorpus.BuildScene(ParaViewCorpus.VTI_CONTOUR, Path.Combine(m_root, "unicode_twin"), m_blobs);
        var twinJob = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var twinStatus = await m_engine.ScheduleAndWaitAsync(twinJob, twin, options);
        Assert.That(twinStatus.Result, Is.EqualTo(WitProcessingResult.Completed), $"{twinStatus.Result}: {twinStatus.Message}");
        Assert.That(Digest(m_blobs.GetStoredPath((Guid)job.Variables["result"].Value!)), Is.EqualTo(Digest(m_blobs.GetStoredPath((Guid)twinJob.Variables["result"].Value!))));
    }

    [Test]
    public async Task WallClockLimitKillsTheWholeRuntimeProcessTreeTest()
    {
        // The real runtime under the real process runner: a pvpython that sleeps past the wall-clock
        // limit must be killed with its whole tree — on Linux the official bin/pvpython is a launcher
        // whose pvpython-real child would otherwise keep burning a node core after the task is gone.
        var home = Path.Combine(m_root, "kill-home");
        var temp = Path.Combine(m_root, "kill-tmp");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(temp);
        var environment = ParaViewRunnerEnvironment.Build(m_pvpython, home, temp, OperatingSystem.IsLinux() ? ParaViewRunnerEnvironment.OSMESA_WINDOW : null);
        var marker = $"outwit_kill_marker_{Guid.NewGuid():N}";
        var script = Path.Combine(m_root, $"{marker}.py");
        File.WriteAllText(script, "import time\nprint('sleeping', flush=True)\ntime.sleep(120)\n");

        var started = DateTime.UtcNow;
        var outcome = await ParaViewProcessRunner.RunAsync(
            m_pvpython, ["--force-offscreen-rendering", "--disable-registry", script], m_root, environment, TimeSpan.FromSeconds(5), null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.TimedOut, Is.True, "the wall-clock limit must report a timeout");
            Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(60)));
        });

        // Nothing of that tree may survive: look for any live process whose command line carries the marker.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.That(LiveProcessesMentioning(marker), Is.Empty, "a runtime process survived the kill");
    }

    [Test]
    public async Task TransparentPngCarriesAlphaTest()
    {
        var (scene, _) = ParaViewCorpus.BuildScene(ParaViewCorpus.SPHERE_STATIC, Path.Combine(m_root, "alpha"), m_blobs);
        var options = new ParaViewOutputOptionsData { Width = 64, Height = 64, TransparentBackground = true };

        var job = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status.Result}: {status.Message}");
        Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath((Guid)job.Variables["result"].Value!))!.HasAlpha, Is.True);
    }

    [Test]
    public async Task NodeBenchmarkMeasuresFullTaskCyclesThroughTheRealRuntimeTest()
    {
        // The benchmark a worker runs at startup, through the real pvpython: complete task cycles
        // (a fresh process per frame — the fleet's task shape, where startup dominates a small frame),
        // yielding the pixels-per-second rate the grid allocator weighs nodes by.
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(1), WarmupIterations = 1 };

        var result = await WitEngineNodeSdk.Instance.RunBenchmark("ParaView.RenderFrame", options);

        TestContext.Out.WriteLine($"ParaView.RenderFrame: {result.Rate:N0} {result.Unit}, cycles={result.Iterations}, elapsed={result.Elapsed}, custom={string.Join(", ", result.Custom?.Select(me => $"{me.Key}={me.Value}") ?? [])}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Unit, Is.EqualTo(ParaViewBenchmark.UNIT));
            Assert.That(result.DatasetId, Is.EqualTo(ParaViewBenchmark.DATASET_ID));
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.Iterations, Is.EqualTo(ParaViewBenchmark.MIN_CYCLES), "a tiny target still measures the minimum cycles");
            Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.FromSeconds(1)), "a real task cycle cannot be sub-second: pvpython startup alone costs seconds");
            Assert.That(result.Custom?[ParaViewBenchmark.CUSTOM_PARAVIEW_VERSION], Does.StartWith(ParaViewRuntimeInfo.RUNTIME_SERIES));
            Assert.That(result.Custom?[ParaViewBenchmark.CUSTOM_RENDER_WINDOW], Does.EndWith("RenderWindow"));
            Assert.That(result.Custom?[ParaViewBenchmark.CUSTOM_SCENE_POINTS], Is.EqualTo("226981"), "61^3 Wavelet points");
        });
    }

    #endregion

    #region Tools

    private string Script(string fileName)
    {
        return File.ReadAllText(Path.Combine(m_scriptsPath, fileName));
    }

    private static IReadOnlyList<string> LiveProcessesMentioning(string marker)
    {
        var found = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            // WMI-free: PowerShell lists command lines of live processes.
            var start = new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -Command \"Get-CimInstance Win32_Process | Where-Object {{ $_.CommandLine -like '*{marker}*' -and $_.Name -notlike 'powershell*' }} | ForEach-Object {{ $_.ProcessId.ToString() + ' ' + $_.Name }}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            found.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(me => me.Trim()).Where(me => me.Length > 0));
        }
        else
        {
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out var pid) || pid == Environment.ProcessId)
                    continue;

                try
                {
                    var commandLine = File.ReadAllText(Path.Combine(directory, "cmdline")).Replace(' ', ' ');
                    if (commandLine.Contains(marker, StringComparison.Ordinal))
                        found.Add($"{pid} {commandLine}");
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return found;
    }

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    #endregion
}
