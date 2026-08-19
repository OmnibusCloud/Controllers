using System.Runtime.InteropServices;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// Binary resolution per RID, the environment allowlist, the argument array, and the runner
/// documents' JSON shape — the pieces of the host contract that need no runtime to verify.
/// </summary>
[TestFixture]
public sealed class ParaViewRuntimeResolutionTests
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

    #region Binary resolver

    [Test]
    public void EnvironmentOverrideWinsEvenWhenMissingTest()
    {
        var configured = Path.Combine(m_dir, "nowhere", "pvpython.exe");
        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, configured);

        Assert.That(ParaViewBinaryResolver.Resolve(Path.Combine(m_dir, "module", "x.dll")), Is.EqualTo(configured));
    }

    [Test]
    public void BundledRuntimeIsFoundUnderTheModuleForThisPlatformTest()
    {
        var folder = ParaViewBinaryResolver.ResolveCurrentRuntimeFolder();
        if (folder == null)
            Assert.Ignore("unsupported platform");

        var module = Path.Combine(m_dir, "paraview.module");
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pvpython.exe" : "pvpython";
        var binDir = Path.Combine(module, ParaViewBinaryResolver.TOOL_DIRECTORY, folder, "ParaView-6.1.1", "bin");
        Directory.CreateDirectory(binDir);
        var exe = Path.Combine(binDir, exeName);
        File.WriteAllText(exe, "#!/bin/sh\n");

        var resolved = ParaViewBinaryResolver.Resolve(Path.Combine(module, "OutWit.Controller.Visualization.ParaView.dll"));

        Assert.That(resolved, Is.EqualTo(exe));
    }

    [Test]
    public void MissingRuntimeResolvesNullTest()
    {
        var module = Path.Combine(m_dir, "paraview.module");
        Directory.CreateDirectory(module);

        Assert.That(ParaViewBinaryResolver.Resolve(Path.Combine(module, "x.dll")), Is.Null);
    }

    [Test]
    public void FindExecutablePrefersBinTest()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pvpython.exe" : "pvpython";
        Directory.CreateDirectory(Path.Combine(m_dir, "bin"));
        Directory.CreateDirectory(Path.Combine(m_dir, "deep", "er"));
        File.WriteAllText(Path.Combine(m_dir, "bin", exeName), "");
        File.WriteAllText(Path.Combine(m_dir, "deep", "er", exeName), "");

        Assert.That(ParaViewBinaryResolver.FindExecutable(m_dir), Is.EqualTo(Path.Combine(m_dir, "bin", exeName)));
    }

    #endregion

    #region Environment

    [Test]
    public void EnvironmentIsAllowlistedAndTaskPrivateTest()
    {
        Environment.SetEnvironmentVariable("DISPLAY", ":0");
        Environment.SetEnvironmentVariable("PV_PLUGIN_PATH", "/evil");
        Environment.SetEnvironmentVariable("OMNIBUSCLOUD_API_KEY_TEST_LEAK", "secret");
        try
        {
            var home = Path.Combine(m_dir, "home");
            var temp = Path.Combine(m_dir, "tmp");
            var pvpython = Path.Combine(m_dir, "rt", "bin", "pvpython");
            var environment = ParaViewRunnerEnvironment.Build(pvpython, home, temp, forceSoftwareRendering: true);

            Assert.Multiple(() =>
            {
                Assert.That(environment.Keys, Does.Not.Contain("DISPLAY"));
                Assert.That(environment.Keys, Does.Not.Contain("PV_PLUGIN_PATH"));
                Assert.That(environment.Keys, Does.Not.Contain("OMNIBUSCLOUD_API_KEY_TEST_LEAK"));
                Assert.That(environment["PATH"], Does.StartWith(Path.GetDirectoryName(pvpython)!));
                Assert.That(environment[ParaViewRunnerEnvironment.VTK_DEFAULT_OPENGL_WINDOW], Is.EqualTo(ParaViewRunnerEnvironment.OSMESA_WINDOW));
                Assert.That(environment["PYTHONNOUSERSITE"], Is.EqualTo("1"));
                Assert.That(environment["PYTHONDONTWRITEBYTECODE"], Is.EqualTo("1"));

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.That(environment["USERPROFILE"], Is.EqualTo(home));
                    Assert.That(environment["APPDATA"], Is.EqualTo(home));
                    Assert.That(environment["TEMP"], Is.EqualTo(temp));
                    Assert.That(environment.Keys, Does.Contain("SystemRoot"));
                }
                else
                {
                    Assert.That(environment["HOME"], Is.EqualTo(home));
                    Assert.That(environment["TMPDIR"], Is.EqualTo(temp));
                    Assert.That(environment["LC_ALL"], Is.EqualTo("C.UTF-8"));
                }
            });

            var withoutSoftware = ParaViewRunnerEnvironment.Build(pvpython, home, temp, forceSoftwareRendering: false);
            Assert.That(withoutSoftware.Keys, Does.Not.Contain(ParaViewRunnerEnvironment.VTK_DEFAULT_OPENGL_WINDOW));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", null);
            Environment.SetEnvironmentVariable("PV_PLUGIN_PATH", null);
            Environment.SetEnvironmentVariable("OMNIBUSCLOUD_API_KEY_TEST_LEAK", null);
        }
    }

    #endregion

    #region Arguments and documents

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
            OutputPath = "/w/out/frame_000001.png",
            StatusPath = "/w/status.json",
            ViewId = "RenderView1",
            TimestepIndex = 1,
            TimeValue = 0.5,
            Width = 320,
            Height = 200,
            Format = "png",
            TransparentBackground = true,
            PluginPath = null,
            AllowedProxies = ["views/RenderView"],
            BlockedProxyTypes = [.. ParaViewProxyPolicy.BLOCKED_PROXY_TYPES],
            FileReferenceGroups = ["sources"],
            MaxStateBytes = 123,
            MaxLogicalPathChars = 77
        };

        var json = task.ToJson();
        var restored = ParaViewRunnerTask.FromJson(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"task_id\""));
            Assert.That(json, Does.Contain("\"timestep_index\": 1"));
            Assert.That(json, Does.Contain("\"plugin_path\": null"));
            Assert.That(json, Does.Contain("\"transparent_background\": true"));
            Assert.That(restored.ViewId, Is.EqualTo("RenderView1"));
            Assert.That(restored.TimeValue, Is.EqualTo(0.5));
            Assert.That(restored.AllowedProxies, Is.EqualTo(new[] { "views/RenderView" }));
            Assert.That(json, Does.Contain("\"max_state_bytes\": 123"));
            Assert.That(restored.MaxLogicalPathChars, Is.EqualTo(77));
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

    [TestCase("6.1.1", "6.1", true)]
    [TestCase("6.1.1-fake", "6.1", true)]
    [TestCase("paraview version 6.1.0", "6.1", false)]
    [TestCase("6.2.0", "6.1", false)]
    [TestCase("5.13.3", "6.1", false)]
    [TestCase("", "6.1", false)]
    [TestCase(null, "6.1", false)]
    public void RuntimeSeriesCheckTest(string? version, string series, bool expected)
    {
        Assert.That(ParaViewRuntimeInfo.IsSameSeries(version, series), Is.EqualTo(expected));
    }

    [Test]
    public void RuntimeInfoStringsDeriveFromTheNumbersTest()
    {
        Assert.That(ParaViewRuntimeInfo.RUNTIME_SERIES, Is.EqualTo($"{ParaViewRuntimeInfo.RUNTIME_MAJOR}.{ParaViewRuntimeInfo.RUNTIME_MINOR}"));
        Assert.That(ParaViewRuntimeInfo.RUNTIME_VERSION, Is.EqualTo($"{ParaViewRuntimeInfo.RUNTIME_SERIES}.{ParaViewRuntimeInfo.RUNTIME_PATCH}"));
        Assert.That(ParaViewProxyAllowlist.Bundled.RuntimeVersion, Is.EqualTo(ParaViewRuntimeInfo.RUNTIME_SERIES));
    }

    [Test]
    public void EmbeddedRunnerIsPresentAndReaderIsNotYetTest()
    {
        Assert.That(ParaViewRuntimeInfo.ReadEmbeddedText(ParaViewRuntimeInfo.RUNNER_RESOURCE), Does.Contain("--task-file"));
        Assert.That(ParaViewRuntimeInfo.ReadEmbeddedText(ParaViewRuntimeInfo.FRD_READER_RESOURCE), Is.Null, "the bundled reader ships with the reader milestone; update this test then");
        Assert.That(ParaViewRuntimeInfo.BundledReaderVersion(), Is.Null);
    }

    #endregion
}
