using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// The node-side task pipeline against the fake pvpython: materialization (subset only, digests
/// verified), the runner contract (task/status documents, cwd, environment), output validation and
/// cleanup — plus every failure path: runner failure, wrong output, missing status, stray output,
/// digest mismatch, a reference outside the subset, cancellation, and the wall-clock limit.
/// </summary>
[TestFixture]
public sealed class ParaViewTaskExecutorTests
{
    #region Fields

    private string m_root = null!;

    private ParaViewTestBlobService m_blobs = null!;

    private IWitTempStorage m_tempStorage = null!;

    private string m_fakePvpython = null!;

    private ParaViewTaskExecutor m_executor = null!;

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
        m_root = Path.Combine(Path.GetTempPath(), $"pv_exec_{Guid.NewGuid():N}");
        m_blobs = new ParaViewTestBlobService(Path.Combine(m_root, "blobs"));
        m_tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "temp"));
        m_executor = new ParaViewTaskExecutor(m_blobs, m_tempStorage, ParaViewProxyAllowlist.LoadEmbedded(ParaViewRuntimeInfo.RUNTIME_SERIES), NullLogger.Instance);
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
    public async Task SuccessfulTaskPublishesAValidatedResultAndCleansUpTest()
    {
        var task = BuildTask(ParaViewStateBuilder.Typical("data/field_1.vtu").WithTimesteps(0, 1, 2).Build(), timestepIndex: 1);

        var result = await m_executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.TaskId, Is.EqualTo(task.TaskId));
            Assert.That(result.TaskIndex, Is.EqualTo(task.TaskIndex));
            Assert.That(result.TimestepIndex, Is.EqualTo(1));
            Assert.That(result.TimeValue, Is.EqualTo(1.0));
            Assert.That(result.Width, Is.EqualTo(64));
            Assert.That(result.Height, Is.EqualTo(48));
            Assert.That(result.Format, Is.EqualTo(ParaViewImageFormat.Png));
            Assert.That(result.ByteSize, Is.GreaterThan(0));
            Assert.That(result.RuntimeVersion, Is.EqualTo("6.1.1-fake"));
            Assert.That(result.Diagnostics, Does.Contain("stage=done"));
            Assert.That(File.Exists(m_blobs.GetStoredPath(result.ImageBlobId)), Is.True);
        });

        var image = ParaViewImageInfo.TryRead(m_blobs.GetStoredPath(result.ImageBlobId));
        Assert.That(image, Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 64, 48, false)));

        // Only the subset was requested from blob storage: the state + the one attachment of timestep 1.
        Assert.That(m_blobs.Requests, Is.EquivalentTo(new[] { task.StateBlobId, task.Attachments.Single().BlobId }));

        // The workspace is gone.
        Assert.That(Directory.Exists(Path.Combine(m_tempStorage.RootPath, "witcloud_paraview")) && Directory.EnumerateFileSystemEntries(Path.Combine(m_tempStorage.RootPath, "witcloud_paraview"), "*", SearchOption.AllDirectories).Any(File.Exists), Is.False);
    }

    [Test]
    public async Task JpegAndTransparentPngAreValidatedTest()
    {
        var jpegTask = BuildTask(ParaViewStateBuilder.Typical("data/field_0.vtu").Build(), format: ParaViewImageFormat.Jpeg);
        var jpeg = await m_executor.ExecuteAsync(jpegTask, Guid.NewGuid(), m_fakePvpython, CancellationToken.None);
        Assert.That(jpeg.Format, Is.EqualTo(ParaViewImageFormat.Jpeg));

        var transparentTask = BuildTask(ParaViewStateBuilder.Typical("data/field_0.vtu").Build(), transparent: true);
        var transparent = await m_executor.ExecuteAsync(transparentTask, Guid.NewGuid(), m_fakePvpython, CancellationToken.None);
        Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath(transparent.ImageBlobId))!.HasAlpha, Is.True);
    }

    [Test]
    public void RunnerFailureSurfacesTheStatusErrorTest()
    {
        var task = BuildTask(ParaViewStateBuilder.Typical("data/field_0.vtu").WithExtraStateContent("<!-- FAKE-FAIL -->").Build());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("exited with code 3"));
        Assert.That(exception.Message, Does.Contain("fake failure requested by the state"));
    }

    [Test]
    public void WrongOutputSizeIsRejectedTest()
    {
        var task = BuildTask(ParaViewStateBuilder.Typical("data/field_0.vtu").WithExtraStateContent("<!-- FAKE-WRONG-SIZE -->").Build());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("65x48"));
    }

    [Test]
    public void MissingStatusIsRejectedDespiteExitZeroTest()
    {
        var task = BuildTask(ParaViewStateBuilder.Typical("data/field_0.vtu").WithExtraStateContent("<!-- FAKE-NO-STATUS -->").Build());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("wrote no status document"));
    }

    [Test]
    public void StrayOutputIsRejectedTest()
    {
        var task = BuildTask(ParaViewStateBuilder.Typical("data/field_0.vtu").WithExtraStateContent("<!-- FAKE-EXTRA-OUTPUT -->").Build());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("stray.txt"));
    }

    [Test]
    public void ReferenceOutsideTheSubsetIsRefusedByTheRunnerTest()
    {
        // The state references field_2 but the task's subset carries only field_0.
        var task = BuildTask(ParaViewStateBuilder.Typical("data/field_2.vtu").Build(), timestepIndex: 0);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("no materialized package file"));
    }

    [Test]
    public async Task FileSeriesWithOnlyItsOwnPieceMaterializedIsAcceptedTest()
    {
        // A file-series reader lists every piece; task 1 materializes only field_1 (+ nothing else).
        var state = new ParaViewStateBuilder().WithTimesteps(0, 1, 2);
        var reader = state.AddReader("XMLUnstructuredGridReader", "field", "data/field_0.vtu", "data/field_1.vtu", "data/field_2.vtu");
        state.AddRepresentation("UnstructuredGridRepresentation", reader);
        state.AddRenderView();
        var task = BuildTask(state.Build(), timestepIndex: 1);

        var result = await m_executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None);

        Assert.That(result.TimestepIndex, Is.EqualTo(1));
        Assert.That(m_blobs.Requests, Is.EquivalentTo(new[] { task.StateBlobId, task.Attachments.Single().BlobId }));
    }

    [Test]
    public void DigestMismatchAbortsBeforeTheRunnerTest()
    {
        var task = BuildTask(ParaViewStateBuilder.Typical("data/field_0.vtu").Build());
        task.Attachments[0].Sha256 = new string('0', 64);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => m_executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("digest mismatch"));
    }

    [Test]
    public void CancellationKillsTheRunnerTest()
    {
        var task = BuildTask(ParaViewStateBuilder.Typical("data/field_0.vtu").WithExtraStateContent("<!-- FAKE-HANG -->").Build());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var started = DateTime.UtcNow;
        Assert.CatchAsync<OperationCanceledException>(() => m_executor.ExecuteAsync(task, Guid.NewGuid(), m_fakePvpython, cts.Token));

        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(60)));
    }

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
            ParaViewRunnerEnvironment.Build(m_fakePvpython, m_root, m_root, false),
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
            ParaViewRunnerEnvironment.Build(m_fakePvpython, m_root, m_root, false),
            TimeSpan.FromSeconds(30), NullLogger.Instance, CancellationToken.None);

        Assert.That(outcome.ExitCode, Is.EqualTo(2));
        Assert.That(outcome.StderrTail, Does.Contain("--task-file"));
        Assert.That(outcome.TimedOut, Is.False);
    }

    #endregion

    #region Tools

    private ParaViewRenderTaskData BuildTask(string stateXml, int timestepIndex = 0, ParaViewImageFormat format = ParaViewImageFormat.Png, bool transparent = false)
    {
        var package = new ParaViewPackageBuilder(Path.Combine(m_root, "pkg_" + Guid.NewGuid().ToString("N")), m_blobs)
            .AddFile("data/field_0.vtu", "field 0", seriesGroup: "field", timestepIndices: [0])
            .AddFile("data/field_1.vtu", "field 1", seriesGroup: "field", timestepIndices: [1])
            .AddFile("data/field_2.vtu", "field 2", seriesGroup: "field", timestepIndices: [2]);
        var scene = package.BuildScene(stateXml, timestepValues: [0, 1, 2]);
        var options = new ParaViewOutputOptionsData { ViewId = "RenderView1", Width = 64, Height = 48, Format = format, TransparentBackground = transparent };
        var report = new ParaViewValidationReportData
        {
            IsValid = true,
            ResolvedViewId = "RenderView1",
            ResolvedTimestepIndices = [timestepIndex],
            TimestepValues = [0, 1, 2],
            PackageDigest = ParaViewPackageDigest.ComputePackageDigest(scene)
        };

        m_blobs.ClearRequests();
        return ParaViewTaskSplitter.Split(scene, report, options).Single();
    }

    #endregion
}
