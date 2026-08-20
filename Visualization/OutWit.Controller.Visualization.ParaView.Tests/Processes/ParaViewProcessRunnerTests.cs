using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;
using OutWit.Controller.Visualization.ParaView.Processes;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;

namespace OutWit.Controller.Visualization.ParaView.Tests.Processes;

/// <summary>
/// The pvpython process runner: wall-clock kill, bounded stderr capture, exit codes — driven by the fake pvpython.
/// </summary>
[TestFixture]
public sealed class ParaViewProcessRunnerTests
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
        m_root = Path.Combine(Path.GetTempPath(), $"pv_proc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
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
    public async Task WallClockLimitKillsTheRunnerTest()
    {
        var runnerPath = Path.Combine(m_root, "render_task.py");
        File.WriteAllText(runnerPath, "# fake");
        var state = Path.Combine(m_root, "state.pvsm");
        File.WriteAllText(state, ParaViewStateBuilder.Typical("data/field_0.vtu").WithExtraStateContent("<!-- FAKE-HANG -->").Build());
        var package = Path.Combine(m_root, "package", "data");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "field_0.vtu"), "x");
        var taskFile = Path.Combine(m_root, "task.json");
        File.WriteAllText(taskFile, new ParaViewRunnerTask
        {
            StatePath = state,
            PackageRoot = Path.Combine(m_root, "package"),
            WorkDir = m_root,
            OutputPath = Path.Combine(m_root, "out.png"),
            StatusPath = Path.Combine(m_root, "status.json"),
            ViewId = "RenderView1",
            Width = 4,
            Height = 4,
            FileReferenceGroups = ["sources"]
        }.ToJson());

        var outcome = await ParaViewProcessRunner.RunAsync(
            m_fakePvpython,
            ParaViewTaskExecutor.BuildArguments(runnerPath, taskFile),
            Path.Combine(m_root, "package"),
            ParaViewRunnerEnvironment.Build(m_fakePvpython, m_root, m_root, null),
            TimeSpan.FromSeconds(2),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.That(outcome.TimedOut, Is.True);
        Assert.That(outcome.ExitCode, Is.EqualTo(-1));
    }

    [Test]
    public async Task ProcessRunnerCapturesBoundedStderrTest()
    {
        var outcome = await ParaViewProcessRunner.RunAsync(
            m_fakePvpython, ["--no-such"], m_root,
            ParaViewRunnerEnvironment.Build(m_fakePvpython, m_root, m_root, null),
            TimeSpan.FromSeconds(30), NullLogger.Instance, CancellationToken.None);

        Assert.That(outcome.ExitCode, Is.EqualTo(2));
        Assert.That(outcome.StderrTail, Does.Contain("--task-file"));
        Assert.That(outcome.TimedOut, Is.False);
    }

    #endregion
}
