using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Output;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Visualization.ParaView.Tests.Activities;

/// <summary>
/// The composed-scene scripts (docs 06, part A) running through the engine end to end — host engine
/// + worker-node engine, blob transport, Grid dispatch, the fake pvpython behind OUTWIT_PVPYTHON:
/// Grid.Delegate ParaView.Compose on the node (materialize the data, compose, publish the state) →
/// the UNCHANGED validate → split → RenderFrame → collect chain; plus the admission failure path and
/// the compose benchmark reached through the engine's per-activity benchmark pass.
/// </summary>
[TestFixture]
public sealed class ParaViewComposeEndToEndTests
{
    #region Constants

    private const string LOGICAL_PATH = "data/model.frd";

    private const string FAKE_FRD = "    1C fake CalculiX result for the fake composer\n";

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
        var solutionRoot = ParaViewTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var controllersPath = ParaViewTestPaths.FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_scriptsPath = ParaViewTestPaths.FindBundledScriptsPath() ?? string.Empty;
        if (m_scriptsPath.Length == 0)
            Assert.Ignore("@Scripts not found");

        var fake = ParaViewTestPaths.FindFakePvpythonPath(solutionRoot);
        if (fake == null)
            Assert.Ignore("fake-pvpython not built");

        Environment.SetEnvironmentVariable(ParaViewBinaryResolver.ENV_PVPYTHON_PATH, fake);

        m_root = Path.Combine(Path.GetTempPath(), $"pv_compose_e2e_{Guid.NewGuid():N}");
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

        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task DataStillJobComposesOnTheNodeAndRendersOneBlobTest()
    {
        var package = NewPackage().AddFile(LOGICAL_PATH, FAKE_FRD + "FAKE-TIMESTEPS=2\n");
        var data = DataScene(package);
        data.ColorArrayName = "DISP";
        var options = new ParaViewOutputOptionsData { Width = 24, Height = 12, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 1 } };

        m_blobs.ClearRequests();
        var job = m_engine.Compile(Script("RenderParaViewDataStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, data, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status}: {status.Message}");

        var scene = job.Variables["scene"].Value as ParaViewSceneRefData;
        var report = job.Variables["report"].Value as ParaViewValidationReportData;
        var result = job.Variables["result"].Value;

        Assert.Multiple(() =>
        {
            Assert.That(scene, Is.Not.Null, "Grid.Delegate hands the composed reference to the chain");
            Assert.That(scene!.StateBlobId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(scene.Attachments.Single().BlobId, Is.EqualTo(package.BlobOf(LOGICAL_PATH)));
            Assert.That(scene.Attachments.Single().Sha256, Is.EqualTo(package.Attachments.Single().Sha256), "the composer stamps the digest of what it materialized");
            Assert.That(scene.TimestepValues, Is.EqualTo(new[] { 1.0, 2.0 }));
            Assert.That(scene.Runtime.Plugins.Single().Name, Is.EqualTo(ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME));
            Assert.That(report, Is.Not.Null);
            Assert.That(report!.IsValid, Is.True, string.Join("; ", report.Errors));
            Assert.That(report.RequiredPlugins, Has.Some.StartsWith(ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME));
            Assert.That(result, Is.TypeOf<Guid>());
        });

        Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath((Guid)result!)), Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 24, 12, false)));

        var stateText = await File.ReadAllTextAsync(m_blobs.GetStoredPath(scene!.StateBlobId));
        Assert.That(stateText, Does.Contain(LOGICAL_PATH));

        // The data blob was fetched by the composer and by the one render task; the state by Validate
        // and by the render task.
        var requests = m_blobs.Requests.GroupBy(me => me).ToDictionary(me => me.Key, me => me.Count());
        Assert.Multiple(() =>
        {
            Assert.That(requests[package.BlobOf(LOGICAL_PATH)], Is.EqualTo(2));
            Assert.That(requests[scene.StateBlobId], Is.EqualTo(2));
        });
    }

    [Test]
    public async Task DataFramesJobRendersEveryTimestepOfTheComposedTimelineTest()
    {
        var package = NewPackage().AddFile(LOGICAL_PATH, FAKE_FRD + "FAKE-TIMESTEPS=4\n");
        var options = new ParaViewOutputOptionsData { Width = 16, Height = 8, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

        var job = m_engine.Compile(Script("RenderParaViewDataFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, DataScene(package), options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status}: {status.Message}");

        var batches = job.Variables["tasks"].Value as IReadOnlyList<ParaViewRenderTaskBatchData?>;
        var result = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.Multiple(() =>
        {
            Assert.That(batches, Has.Count.EqualTo(4), "one task per timestep the composer reported, one output per batch below the policy's target");
            Assert.That(result, Has.Count.EqualTo(4));
        });

        foreach (var batch in batches!.Select(me => me!))
            Assert.That(batch.Attachments.Select(me => me.LogicalPath), Is.EqualTo(new[] { LOGICAL_PATH }), "the .frd is timestep-independent: every batch carries it");

        foreach (var blobId in result!.Select(me => me!.Value))
            Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath(blobId)), Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 16, 8, false)));
    }

    [Test]
    public async Task DataStillJobWithTurntableComposesOnceAndRendersTheOrbitTest()
    {
        var package = NewPackage().AddFile(LOGICAL_PATH, FAKE_FRD + "FAKE-TIMESTEPS=1\n");
        var options = new ParaViewOutputOptionsData
        {
            Width = 16,
            Height = 8,
            Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 0 },
            Turntable = new ParaViewTurntableData { Frames = 3, Degrees = 360 }
        };

        m_blobs.ClearRequests();
        var job = m_engine.Compile(Script("RenderParaViewDataFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, DataScene(package), options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status}: {status.Message}");

        var scene = (job.Variables["scene"].Value as ParaViewSceneRefData)!;
        var result = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        var requests = m_blobs.Requests.GroupBy(me => me).ToDictionary(me => me.Key, me => me.Count());
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(3), "three orbit outputs of the one timestep");
            Assert.That(requests[package.BlobOf(LOGICAL_PATH)], Is.EqualTo(4), "composed once, materialized by three render tasks");
        });
        Assert.That(scene.StateBlobId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task DataFramesJobWithARiseRendersEveryOutputOfTheMoveTest()
    {
        var package = NewPackage().AddFile(LOGICAL_PATH, FAKE_FRD + "FAKE-TIMESTEPS=1\n");
        var options = new ParaViewOutputOptionsData
        {
            Width = 16,
            Height = 8,
            Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 0 },
            Turntable = new ParaViewTurntableData { Frames = 3, Degrees = 0.0, ElevationDegrees = 60.0, DollyFactor = 0.5 }
        };

        var job = m_engine.Compile(Script("RenderParaViewDataFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, DataScene(package), options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status}: {status.Message}");

        var tasks = (job.Variables["tasks"].Value as IReadOnlyList<ParaViewRenderTaskBatchData?>)!.SelectMany(me => me!.Tasks).ToList();
        var result = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(3), "a pure rise (no sweep) is a move too");
            Assert.That(tasks.Select(me => me.ElevationDegrees), Is.EqualTo(new[] { 0.0, 30.0, 60.0 }).Within(1e-9));
            Assert.That(tasks.Select(me => me.DollyFactor), Is.EqualTo(new[] { 1.0, Math.Sqrt(0.5), 0.5 }).Within(1e-9));
            Assert.That(tasks.Select(me => me.TaskId).Distinct().Count(), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ValidateDataJobReturnsTheReportOfTheComposedStateTest()
    {
        var package = NewPackage().AddFile(LOGICAL_PATH, FAKE_FRD);

        var job = m_engine.Compile(Script("ValidateParaViewData.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, DataScene(package), new ParaViewOutputOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status}: {status.Message}");

        var report = job.Variables["result"].Value as ParaViewValidationReportData;
        Assert.That(report, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(report!.IsValid, Is.True, string.Join("; ", report.Errors));
            Assert.That(report.TimestepValues, Has.Count.EqualTo(3));
            Assert.That(report.AttachmentCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task InadmissibleDataSceneFailsTheJobAtComposeTest()
    {
        var package = NewPackage().AddFile("data/model.vtu", "<VTKFile/>");
        var data = DataScene(package, "data/model.vtu");

        var job = m_engine.Compile(Script("RenderParaViewDataStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, data, new ParaViewOutputOptionsData { Width = 8, Height = 8 });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(job.Variables["tasks"].Value, Is.Null.Or.Empty);
    }

    [Test]
    public async Task ComposerFailureFailsTheJobBeforeAnyRenderTest()
    {
        var package = NewPackage().AddFile(LOGICAL_PATH, FAKE_FRD + "FAKE-COMPOSE-FAIL\n");

        var job = m_engine.Compile(Script("RenderParaViewDataFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, DataScene(package), new ParaViewOutputOptionsData { Width = 8, Height = 8 });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(job.Variables["rendered"].Value, Is.Null.Or.Empty);
    }

    [Test]
    public async Task NodeBenchmarkOfComposeMeasuresComposeCyclesTest()
    {
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(1), WarmupIterations = 1 };

        var result = await WitEngineNodeSdk.Instance.RunBenchmark("ParaView.Compose", options);

        Assert.Multiple(() =>
        {
            Assert.That(result.Unit, Is.EqualTo(ParaViewComposeBenchmark.UNIT));
            Assert.That(result.DatasetId, Is.EqualTo(ParaViewComposeBenchmark.DATASET_ID));
            Assert.That(result.Iterations, Is.EqualTo(ParaViewComposeBenchmark.MIN_CYCLES), "a tiny target still measures the minimum cycles — and no more than that");
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(result.Custom?[ParaViewComposeBenchmark.CUSTOM_CYCLES], Is.EqualTo(ParaViewComposeBenchmark.MIN_CYCLES.ToString()));
        });
    }

    #endregion

    #region Tools

    private ParaViewPackageBuilder NewPackage()
    {
        return new ParaViewPackageBuilder(Path.Combine(m_root, "pkg_" + Guid.NewGuid().ToString("N")), m_blobs);
    }

    private static ParaViewDataSceneData DataScene(ParaViewPackageBuilder package, string logicalPath = LOGICAL_PATH)
    {
        // The initiator declares only what it holds: the blob id and the logical path (no digest, no
        // size — the composer stamps them), exactly the WitSweep zero-upload case.
        return new ParaViewDataSceneData
        {
            Attachments =
            [
                new ParaViewAttachmentRefData
                {
                    BlobId = package.BlobOf(logicalPath),
                    LogicalPath = logicalPath,
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
