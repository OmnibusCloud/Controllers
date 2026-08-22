using System.Diagnostics;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Output;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// Runs the REAL controller-owned runner (the embedded render_task.py) under a local Python with a
/// stub <c>paraview</c> package on PYTHONPATH, so the runner's own control flow — task file parsing,
/// state resolution, plugin/state load, the post-load policy check, view/timestep selection, the
/// screenshot call, output verification, the status document on every exit path and the exit codes —
/// is exercised end to end rather than only mirrored by the C# fake. Ignored when no python is on PATH.
/// </summary>
[TestFixture]
public sealed class ParaViewRunnerScriptTests
{
    #region Fields

    private string m_python = null!;

    private string m_root = null!;

    private string m_stubDir = null!;

    private string m_runnerPath = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_python = FindPython() ?? string.Empty;
        if (m_python.Length == 0)
            Assert.Ignore("no python interpreter on PATH");
    }

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"pv_runner_{Guid.NewGuid():N}");
        m_stubDir = Path.Combine(m_root, "stub");
        Directory.CreateDirectory(m_stubDir);
        ParaViewStubPackage.WriteTo(m_stubDir);

        m_runnerPath = Path.Combine(m_root, ParaViewRuntimeInfo.RUNNER_FILE_NAME);
        File.WriteAllText(m_runnerPath, ParaViewRuntimeInfo.ReadEmbeddedText(ParaViewRuntimeInfo.RUNNER_RESOURCE)!);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void RunnerRendersATimestepThroughTheStubParaViewTest()
    {
        var state = new ParaViewStateBuilder().WithTimesteps(0, 0.5, 1.0);
        var reader = state.AddReader("XMLUnstructuredGridReader", "field", "data/field_0.vtu", "data/field_1.vtu", "data/field_2.vtu");
        state.AddRepresentation("UnstructuredGridRepresentation", reader);
        state.AddRenderView();
        var (task, workDir) = Prepare(state.Build(), timestepIndex: 1, timeValue: 0.5, materialize: ["data/field_1.vtu"]);

        var (exitCode, status, stderr) = Run(task);

        Assert.That(exitCode, Is.EqualTo(0), stderr);
        Assert.That(status, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(status!.Ok, Is.True, status.Error);
            Assert.That(status.Stage, Is.EqualTo("done"));
            Assert.That(status.ParaviewVersion, Is.EqualTo("6.1.1-stub"));
            Assert.That(status.Width, Is.EqualTo(48));
            Assert.That(status.Height, Is.EqualTo(32));
            Assert.That(status.ProxyCount, Is.GreaterThan(0));
            Assert.That(status.MissingReferences, Is.EqualTo(2));
            Assert.That(status.VtkErrors, Is.EqualTo(0));
            Assert.That(status.Backend, Is.EqualTo("vtkStubRenderWindow"));
        });

        var image = ParaViewImageInfo.TryRead(task.OutputPath);
        Assert.That(image, Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 48, 32, false)));

        // The stub records what the runner asked for.
        var log = File.ReadAllText(Path.Combine(workDir, "stub.log"));
        Assert.That(log, Does.Contain("animation_time=0.5"));
        Assert.That(log, Does.Contain("view=RenderView1"));
        Assert.That(log, Does.Contain("load_state="));
    }

    [Test]
    public void RunnerOrbitsTheCameraAboutItsViewUpTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu").Build();
        var (task, workDir) = Prepare(state, timestepIndex: 0, timeValue: null, materialize: ["data/field.vtu"]);
        task.CameraAzimuth = 135.0;
        task.CameraAxis = ParaViewCameraAxes.VIEW_UP;
        File.WriteAllText(Path.Combine(workDir, "task.json"), task.ToJson());

        var (exitCode, status, stderr) = Run(task);

        Assert.That(exitCode, Is.EqualTo(0), stderr);
        Assert.That(status!.Ok, Is.True, status.Error);

        // The stub records the orbit: vtkCamera.Azimuth before the render, nothing else touched.
        var log = File.ReadAllText(Path.Combine(workDir, "stub.log"));
        Assert.Multiple(() =>
        {
            Assert.That(log, Does.Contain("azimuth=135.0"));
            Assert.That(log, Does.Not.Contain("position=").And.Not.Contain("view_up="));
            Assert.That(log.IndexOf("azimuth=135.0", StringComparison.Ordinal), Is.LessThan(log.IndexOf("render view=", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void RunnerRevolvesTheCameraRigidlyAboutAWorldAxisTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu").Build();
        var (task, workDir) = Prepare(state, timestepIndex: 0, timeValue: null, materialize: ["data/field.vtu"]);
        task.CameraAzimuth = 90.0;
        task.CameraAxis = ParaViewCameraAxes.Y;
        File.WriteAllText(Path.Combine(workDir, "task.json"), task.ToJson());

        var (exitCode, status, stderr) = Run(task);

        Assert.That(exitCode, Is.EqualTo(0), stderr);
        Assert.That(status!.Ok, Is.True, status.Error);

        // The stub camera sits at (0,0,10) looking at the origin, up +Y: a 90-degree right-hand turn
        // about +Y moves it to (10,0,0) and leaves the view-up alone (it lies on the axis).
        var log = File.ReadAllText(Path.Combine(workDir, "stub.log"));
        Assert.Multiple(() =>
        {
            Assert.That(log, Does.Contain("position=10.0000,0.0000,0.0000"));
            Assert.That(log, Does.Contain("view_up=0.0000,1.0000,0.0000"));
            Assert.That(log, Does.Not.Contain("azimuth="));
            Assert.That(log.IndexOf("position=", StringComparison.Ordinal), Is.LessThan(log.IndexOf("render view=", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void RunnerLeavesTheCameraAloneWithoutATurntableTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu").Build();
        var (task, workDir) = Prepare(state, timestepIndex: 0, timeValue: null, materialize: ["data/field.vtu"]);

        var (exitCode, _, stderr) = Run(task);

        Assert.That(exitCode, Is.EqualTo(0), stderr);
        var log = File.ReadAllText(Path.Combine(workDir, "stub.log"));
        Assert.That(log, Does.Not.Contain("azimuth=").And.Not.Contain("view_up="));
    }

    [Test]
    public void RunnerRefusesAReferenceOutsideThePackageBeforeImportingParaViewTest()
    {
        var (task, _) = Prepare(ParaViewStateBuilder.Typical("C:/Users/me/field.vtu").Build(), 0, null, ["data/field.vtu"]);

        var (exitCode, status, _) = Run(task);

        Assert.That(exitCode, Is.EqualTo(3));
        Assert.That(status!.Stage, Is.EqualTo("resolve-state"));
        Assert.That(status.Error, Does.Contain("drive letter"));
    }

    [Test]
    public void RunnerRefusesAProxyOutsideTheAllowlistAfterLoadTest()
    {
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddFilter("SomeExoticFilter", "Exotic1", 1000);
        var (task, _) = Prepare(state.Build(), 0, null, ["data/field.vtu"]);

        var (exitCode, status, _) = Run(task);

        Assert.That(exitCode, Is.EqualTo(3));
        Assert.That(status!.Stage, Is.EqualTo("validate"));
        Assert.That(status.Error, Does.Contain("filters/SomeExoticFilter"));
    }

    [Test]
    public void RunnerRefusesAnUnknownViewTest()
    {
        var (task, _) = Prepare(ParaViewStateBuilder.Typical("data/field.vtu").Build(), 0, null, ["data/field.vtu"], viewId: "Nope");

        var (exitCode, status, _) = Run(task);

        Assert.That(exitCode, Is.EqualTo(3));
        Assert.That(status!.Stage, Is.EqualTo("select"));
        Assert.That(status.Error, Does.Contain("'Nope'"));
    }

    [Test]
    public void RunnerFailsOnVtkErrorsInsteadOfPublishingTest()
    {
        // An element, not a comment: the runner re-serializes the resolved state and comments do not survive.
        var state = ParaViewStateBuilder.Typical("data/field.vtu").WithExtraStateContent("<StubMarker name=\"STUB-VTK-ERROR\"/>");
        var (task, _) = Prepare(state.Build(), 0, null, ["data/field.vtu"]);

        var (exitCode, status, _) = Run(task);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(status!.Ok, Is.False);
        Assert.That(status.VtkErrors, Is.EqualTo(1));
        Assert.That(status.Error, Does.Contain("VTK reported 1 error"));
    }

    [Test]
    public void RunnerRefusesATimestepMismatchTest()
    {
        var (task, _) = Prepare(ParaViewStateBuilder.Typical("data/field.vtu").WithTimesteps(0, 1, 2).Build(), timestepIndex: 2, timeValue: 7.0, materialize: ["data/field.vtu"]);

        var (exitCode, status, _) = Run(task);

        Assert.That(exitCode, Is.EqualTo(3));
        Assert.That(status!.Error, Does.Contain("expects 7.0"));
    }

    [Test]
    public void RunnerWritesStatusForAMissingTaskFileTest()
    {
        var (exitCode, _, stderr) = RunRaw(["--task-file", Path.Combine(m_root, "absent.json")]);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stderr, Does.Contain("cannot read task file"));
    }

    #endregion

    #region Tools

    private (ParaViewRunnerTask Task, string WorkDir) Prepare(string stateXml, int timestepIndex, double? timeValue, string[] materialize, string viewId = "RenderView1")
    {
        var workDir = Path.Combine(m_root, "work_" + Guid.NewGuid().ToString("N")[..8]);
        var packageRoot = Path.Combine(workDir, "package");
        Directory.CreateDirectory(Path.Combine(workDir, "out"));
        Directory.CreateDirectory(packageRoot);

        foreach (var logical in materialize)
        {
            var path = Path.Combine(packageRoot, logical.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "data");
        }

        var statePath = Path.Combine(workDir, "state.pvsm");
        File.WriteAllText(statePath, stateXml);

        var task = new ParaViewRunnerTask
        {
            TaskId = "t",
            StatePath = statePath,
            PackageRoot = packageRoot,
            WorkDir = workDir,
            OutputPath = Path.Combine(workDir, "out", "frame.png"),
            StatusPath = Path.Combine(workDir, "status.json"),
            ViewId = viewId,
            TimestepIndex = timestepIndex,
            TimeValue = timeValue,
            Width = 48,
            Height = 32,
            Format = "png",
            AllowedProxies = [.. ParaViewProxyAllowlist.Bundled.EffectiveKeys([])],
            BlockedProxyTypes = [.. ParaViewProxyPolicy.BLOCKED_PROXY_TYPES],
            BlockedPropertyNames = [.. ParaViewProxyPolicy.BLOCKED_PROPERTY_NAMES],
            FilePropertyNames = [.. ParaViewProxyPolicy.FILE_PROPERTY_NAMES],
            FileReferenceGroups = [.. ParaViewProxyPolicy.FILE_REFERENCE_GROUPS],
            MaxStateBytes = ParaViewInputLimits.MAX_STATE_BYTES,
            MaxLogicalPathChars = ParaViewInputLimits.MAX_LOGICAL_PATH_CHARS
        };

        File.WriteAllText(Path.Combine(workDir, "task.json"), task.ToJson());
        return (task, workDir);
    }

    private (int ExitCode, ParaViewRunnerStatus? Status, string Stderr) Run(ParaViewRunnerTask task)
    {
        var (exitCode, _, stderr) = RunRaw(["--task-file", Path.Combine(task.WorkDir, "task.json")], task.PackageRoot);
        return (exitCode, ParaViewRunnerStatus.TryRead(task.StatusPath), stderr);
    }

    private (int ExitCode, string Stdout, string Stderr) RunRaw(string[] arguments, string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo(m_python)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? m_root
        };
        startInfo.ArgumentList.Add(m_runnerPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["PYTHONPATH"] = m_stubDir;
        startInfo.Environment["PYTHONDONTWRITEBYTECODE"] = "1";

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("the runner did not exit in time");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string? FindPython()
    {
        foreach (var candidate in new[] { "python3", "python" })
        {
            try
            {
                var info = new ProcessStartInfo(candidate, "--version") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var process = Process.Start(info);
                if (process == null)
                    continue;

                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit(10_000);
                if (process.ExitCode == 0 && output.Contains("Python 3", StringComparison.Ordinal))
                    return candidate;
            }
            catch
            {
                // Not on PATH.
            }
        }

        return null;
    }

    #endregion
}
