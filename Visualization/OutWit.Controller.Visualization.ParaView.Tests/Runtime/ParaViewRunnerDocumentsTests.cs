using System.Runtime.InteropServices;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// The runner contract documents (task file, status file) and the argument array.
/// </summary>
[TestFixture]
public sealed class ParaViewRunnerDocumentsTests
{
    #region Fields

    private string m_dir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_dir = Path.Combine(Path.GetTempPath(), $"pv_resolve_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_dir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, null);
        if (Directory.Exists(m_dir))
            Directory.Delete(m_dir, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void ArgumentArrayPlacesOptionsBeforeTheScriptTest()
    {
        var arguments = ParaViewTaskExecutor.BuildArguments("/w/runner/render_task.py", "/w/task.json");

        Assert.That(arguments, Is.EqualTo(new[] { "--force-offscreen-rendering", "--disable-registry", "/w/runner/render_task.py", "--task-file", "/w/task.json" }));
    }

    [Test]
    public void RunnerTaskSerializesSnakeCaseAndRoundTripsTest()
    {
        var task = new ParaViewRunnerTask
        {
            TaskId = "abc",
            StatePath = "/w/state.pvsm",
            PackageRoot = "/w/package",
            WorkDir = "/w",
            StatusPath = "/w/status.json",
            ViewId = "RenderView1",
            Width = 320,
            Height = 200,
            Format = "png",
            TransparentBackground = true,
            PluginPath = null,
            AllowedProxies = ["views/RenderView"],
            BlockedProxyTypes = [.. ParaViewProxyPolicy.BLOCKED_PROXY_TYPES],
            FileReferenceGroups = ["sources"],
            MaxStateBytes = 123,
            MaxLogicalPathChars = 77,
            Outputs =
            [
                new ParaViewRunnerOutput
                {
                    Index = 0,
                    TaskId = "abc",
                    OutputPath = "/w/out/frame_000001.png",
                    TimestepIndex = 1,
                    TimeValue = 0.5,
                    CameraAzimuth = 22.5,
                    CameraAxis = ParaViewCameraAxes.Z,
                    CameraElevation = 12.5,
                    CameraDolly = 0.75
                },
                new ParaViewRunnerOutput { Index = 1, TaskId = "def", OutputPath = "/w/out/frame_000002.png", TimestepIndex = 2, TimeValue = 1.0 }
            ]
        };

        var json = task.ToJson();
        var restored = ParaViewRunnerTask.FromJson(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"schema\": 2"));
            Assert.That(json, Does.Contain("\"task_id\""));
            Assert.That(json, Does.Contain("\"outputs\""));
            Assert.That(json, Does.Contain("\"timestep_index\": 1"));
            Assert.That(json, Does.Contain("\"plugin_path\": null"));
            Assert.That(json, Does.Contain("\"transparent_background\": true"));
            Assert.That(restored.ViewId, Is.EqualTo("RenderView1"));
            Assert.That(restored.Outputs, Has.Count.EqualTo(2));
            Assert.That(restored.Outputs[0].TimeValue, Is.EqualTo(0.5));
            Assert.That(restored.Outputs[1].TimestepIndex, Is.EqualTo(2));
            Assert.That(restored.AllowedProxies, Is.EqualTo(new[] { "views/RenderView" }));
            Assert.That(json, Does.Contain("\"max_state_bytes\": 123"));
            Assert.That(restored.MaxLogicalPathChars, Is.EqualTo(77));
            Assert.That(json, Does.Contain("\"camera_azimuth\": 22.5"));
            Assert.That(json, Does.Contain("\"camera_axis\": \"z\""));
            Assert.That(restored.Outputs[0].CameraAzimuth, Is.EqualTo(22.5));
            Assert.That(restored.Outputs[0].CameraAxis, Is.EqualTo("z"));
            Assert.That(json, Does.Contain("\"camera_elevation\": 12.5"));
            Assert.That(json, Does.Contain("\"camera_dolly\": 0.75"));
            Assert.That(restored.Outputs[0].CameraElevation, Is.EqualTo(12.5));
            Assert.That(restored.Outputs[0].CameraDolly, Is.EqualTo(0.75));
            Assert.That(restored.Outputs[1].CameraDolly, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void RunnerStatusReadsThePerOutputVerdictsTest()
    {
        var path = Path.Combine(m_dir, "status.json");
        File.WriteAllText(path, "{\"schema\":2,\"ok\":false,\"stage\":\"render\",\"error\":\"output 2 of 2: boom\",\"render_seconds\":0.5,\"outputs\":[{\"index\":0,\"ok\":true,\"stage\":\"done\",\"error\":\"\",\"render_seconds\":0.5},{\"index\":1,\"ok\":false,\"stage\":\"render\",\"error\":\"boom\",\"render_seconds\":0.0}]}");

        var status = ParaViewRunnerStatus.TryRead(path);

        Assert.That(status, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(status!.Ok, Is.False);
            Assert.That(status.Outputs, Has.Count.EqualTo(2));
            Assert.That(status.Outputs[0].Ok, Is.True);
            Assert.That(status.FirstFailedOutput()?.Index, Is.EqualTo(1));
            Assert.That(status.FirstFailedOutput()?.Error, Is.EqualTo("boom"));
        });
    }

    [Test]
    public void RunnerStatusReadsSnakeCaseAndToleratesGarbageTest()
    {
        var path = Path.Combine(m_dir, "status.json");
        File.WriteAllText(path, "{\"schema\":1,\"ok\":true,\"stage\":\"done\",\"paraview_version\":\"6.1.1\",\"render_seconds\":1.25,\"proxy_count\":12,\"width\":8,\"height\":4,\"backend\":\"vtkOSOpenGLRenderWindow\"}");

        var status = ParaViewRunnerStatus.TryRead(path);

        Assert.That(status, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(status!.Ok, Is.True);
            Assert.That(status.Stage, Is.EqualTo("done"));
            Assert.That(status.ParaviewVersion, Is.EqualTo("6.1.1"));
            Assert.That(status.RenderSeconds, Is.EqualTo(1.25));
            Assert.That(status.ProxyCount, Is.EqualTo(12));
            Assert.That(status.Backend, Is.EqualTo("vtkOSOpenGLRenderWindow"));
        });

        File.WriteAllText(path, "{not json");
        Assert.That(ParaViewRunnerStatus.TryRead(path), Is.Null);
        Assert.That(ParaViewRunnerStatus.TryRead(Path.Combine(m_dir, "absent.json")), Is.Null);
    }

    #endregion
}
