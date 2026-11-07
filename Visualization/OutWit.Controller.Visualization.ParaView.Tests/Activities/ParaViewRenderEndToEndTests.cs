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
/// The bundled scripts running through the engine end to end — host engine + worker-node engine,
/// blob transport, Grid dispatch, the fake pvpython behind OUTWIT_PVPYTHON: validate → split into
/// per-timestep tasks → RenderFrame on the node (materialize the subset, run, validate output) →
/// collect the ordered frame set; plus the per-node download accounting that proves no task
/// materialized an attachment outside its subset, and the failure/validation paths.
/// </summary>
[TestFixture]
public sealed class ParaViewRenderEndToEndTests
{
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

        m_root = Path.Combine(Path.GetTempPath(), $"pv_e2e_{Guid.NewGuid():N}");
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
    public async Task FramesJobRendersEveryTimestepWithSubsetOnlyDownloadsTest()
    {
        var package = NewPackage()
            .AddFile("data/mesh.vtu", "static mesh")
            .AddFile("data/field_0.vtu", "field 0", seriesGroup: "field", timestepIndices: [0])
            .AddFile("data/field_1.vtu", "field 1", seriesGroup: "field", timestepIndices: [1])
            .AddFile("data/field_2.vtu", "field 2", seriesGroup: "field", timestepIndices: [2]);

        // The state reads the static mesh and, per timestep, one series piece (FileNames lists all three —
        // the reader's file series — while each task materializes only its own piece plus the mesh).
        var state = new ParaViewStateBuilder().WithTimesteps(0, 0.5, 1.0);
        var mesh = state.AddReader("XMLUnstructuredGridReader", "mesh.vtu", "data/mesh.vtu");
        state.AddRepresentation("UnstructuredGridRepresentation", mesh);
        state.AddRenderView();
        var scene = package.BuildScene(state.Build());
        var options = new ParaViewOutputOptionsData { Width = 32, Height = 16, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

        m_blobs.ClearRequests();
        var job = m_engine.Compile(Script("RenderParaViewFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status}: {status.Message}");

        var report = job.Variables["report"].Value as ParaViewValidationReportData;
        var batches = job.Variables["tasks"].Value as IReadOnlyList<ParaViewRenderTaskBatchData?>;
        var renderedBatches = job.Variables["rendered"].Value as IReadOnlyList<ParaViewRenderResultBatchData?>;
        var result = job.Variables["result"].Value as IReadOnlyList<Guid?>;

        // Three outputs split per output (ceil(3 / 24) = 1): the batch shape with one-output batches.
        var tasks = batches?.Where(me => me != null).SelectMany(me => me!.Tasks).ToList();
        var rendered = renderedBatches?.Where(me => me != null).SelectMany(me => me!.Results).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(report, Is.Not.Null);
            Assert.That(report!.IsValid, Is.True, string.Join("; ", report.Errors));
            Assert.That(batches, Has.Count.EqualTo(3));
            Assert.That(batches!.All(me => me!.Tasks.Count == 1), Is.True);
            Assert.That(tasks, Has.Count.EqualTo(3));
            Assert.That(rendered, Has.Count.EqualTo(3));
            Assert.That(result, Has.Count.EqualTo(3));
        });

        // Collect order = task order = timestep order, whatever order the node completed them in.
        var byIndex = rendered!.Where(me => me != null).OrderBy(me => me!.TaskIndex).Select(me => me!).ToList();
        Assert.That(byIndex.Select(me => me.TimestepIndex), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(result!.Select(me => me!.Value), Is.EqualTo(byIndex.Select(me => me.ImageBlobId)));

        foreach (var blobId in result.Select(me => me!.Value))
        {
            var image = ParaViewImageInfo.TryRead(m_blobs.GetStoredPath(blobId));
            Assert.That(image, Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Png, 32, 16, false)));
        }

        // Download accounting: pieces 1 and 2 were requested exactly once (by their own task), the
        // series anchor (piece 0) and the static mesh once per task, the state once by Validate plus
        // once per task — nothing else.
        var requests = m_blobs.Requests.GroupBy(me => me).ToDictionary(me => me.Key, me => me.Count());
        Assert.Multiple(() =>
        {
            Assert.That(requests[package.BlobOf("data/field_0.vtu")], Is.EqualTo(3));
            Assert.That(requests[package.BlobOf("data/field_1.vtu")], Is.EqualTo(1));
            Assert.That(requests[package.BlobOf("data/field_2.vtu")], Is.EqualTo(1));
            Assert.That(requests[package.BlobOf("data/mesh.vtu")], Is.EqualTo(3));
            Assert.That(requests[scene.StateBlobId], Is.EqualTo(4));
            Assert.That(requests.Keys, Is.SubsetOf(new[] { scene.StateBlobId }.Concat(package.Attachments.Select(me => me.BlobId))));
            Assert.That(report.SeriesAnchors, Is.EqualTo(new[] { "data/field_0.vtu" }));
        });

        foreach (var batch in batches!.Select(me => me!))
            Assert.That(batch.Attachments.Select(me => me.LogicalPath), Is.EquivalentTo(new[] { "data/mesh.vtu", "data/field_0.vtu", $"data/field_{batch.Tasks[0].TimestepIndex}.vtu" }.Distinct()));
    }

    [Test]
    public async Task LongFramesJobRendersInBatchesWithUnionDownloadsTest()
    {
        // FrameBatch through the whole engine: 30 timesteps split into chunks of 2 (ceil(30 / 24)),
        // each chunk one pvpython process over the union of its two subsets - the static mesh and
        // the series anchor are fetched once per CHUNK (15×), every other piece exactly once, and the
        // collected frame set is complete and in timestep order.
        const int timesteps = 30;
        var package = NewPackage().AddFile("data/mesh.vtu", "static mesh");
        for (var i = 0; i < timesteps; i++)
            package.AddFile($"data/field_{i}.vtu", $"field {i}", seriesGroup: "field", timestepIndices: [i]);

        var state = new ParaViewStateBuilder().WithTimesteps(Enumerable.Range(0, timesteps).Select(i => i * 0.1).ToArray());
        var mesh = state.AddReader("XMLUnstructuredGridReader", "mesh.vtu", "data/mesh.vtu");
        state.AddRepresentation("UnstructuredGridRepresentation", mesh);
        state.AddRenderView();
        var scene = package.BuildScene(state.Build());
        var options = new ParaViewOutputOptionsData { Width = 16, Height = 8, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.All } };

        m_blobs.ClearRequests();
        var job = m_engine.Compile(Script("RenderParaViewFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status}: {status.Message}");

        var batches = job.Variables["tasks"].Value as IReadOnlyList<ParaViewRenderTaskBatchData?>;
        var renderedBatches = job.Variables["rendered"].Value as IReadOnlyList<ParaViewRenderResultBatchData?>;
        var result = job.Variables["result"].Value as IReadOnlyList<Guid?>;

        Assert.Multiple(() =>
        {
            Assert.That(batches, Has.Count.EqualTo(15));
            Assert.That(batches!.All(me => me!.Tasks.Count == 2), Is.True, "chunks of two");
            Assert.That(renderedBatches, Has.Count.EqualTo(15));
            Assert.That(result, Has.Count.EqualTo(timesteps));
        });

        var ordered = renderedBatches!.SelectMany(me => me!.Results).OrderBy(me => me.TaskIndex).ToList();
        Assert.That(ordered.Select(me => me.TimestepIndex), Is.EqualTo(Enumerable.Range(0, timesteps)));
        Assert.That(result!.Select(me => me!.Value), Is.EqualTo(ordered.Select(me => me.ImageBlobId)));
        Assert.That(result.Select(me => me!.Value).Distinct().Count(), Is.EqualTo(timesteps), "one blob per frame");

        var requests = m_blobs.Requests.GroupBy(me => me).ToDictionary(me => me.Key, me => me.Count());
        Assert.Multiple(() =>
        {
            Assert.That(requests[package.BlobOf("data/mesh.vtu")], Is.EqualTo(15), "the static mesh once per chunk");
            Assert.That(requests[package.BlobOf("data/field_0.vtu")], Is.EqualTo(15), "the series anchor once per chunk");
            for (var i = 1; i < timesteps; i++)
                Assert.That(requests[package.BlobOf($"data/field_{i}.vtu")], Is.EqualTo(1), $"piece {i} exactly once");
            Assert.That(requests[scene.StateBlobId], Is.EqualTo(16), "the state once by Validate plus once per chunk");
        });
    }

    [Test]
    public async Task StillJobReturnsOneBlobTest()
    {
        var package = NewPackage().AddFile("data/field.vtu", "<VTKFile/>");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithTimesteps(0, 1).Build());
        var options = new ParaViewOutputOptionsData { Width = 20, Height = 10, Format = ParaViewImageFormat.Jpeg, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 1 } };

        var job = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status}: {status.Message}");

        var result = job.Variables["result"].Value;
        Assert.That(result, Is.TypeOf<Guid>());
        Assert.That(ParaViewImageInfo.TryRead(m_blobs.GetStoredPath((Guid)result!)), Is.EqualTo(new ParaViewImageInfo(ParaViewImageFormat.Jpeg, 20, 10, false)));
    }

    [Test]
    public async Task ValidateOnlyJobReturnsTheReportWithoutFailingTest()
    {
        var package = NewPackage().AddFile("data/field.vtu", "x");
        var state = ParaViewStateBuilder.Typical("data/field.vtu");
        state.AddFilter("ProgrammableFilter", "Evil", 1000, ("Script", ["import os"]));
        var scene = package.BuildScene(state.Build());

        var job = m_engine.Compile(Script("ValidateParaViewScene.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, new ParaViewOutputOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"{status}: {status.Message}");

        var report = job.Variables["result"].Value as ParaViewValidationReportData;
        Assert.That(report, Is.Not.Null);
        Assert.That(report!.IsValid, Is.False);
        Assert.That(report.Errors, Has.Some.Contains("ProgrammableFilter"));
    }

    [Test]
    public async Task InvalidPackageFailsTheRenderJobAtSplitTest()
    {
        var package = NewPackage().AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("C:/Users/me/field.vtu").Build());

        var job = m_engine.Compile(Script("RenderParaViewFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, new ParaViewOutputOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(job.Variables["tasks"].Value, Is.Null.Or.Empty);
    }

    [Test]
    public async Task RunnerFailureFailsTheJobTest()
    {
        var package = NewPackage().AddFile("data/field.vtu", "x");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithExtraStateContent("<!-- FAKE-FAIL -->").Build());

        var job = m_engine.Compile(Script("RenderParaViewFrames.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, new ParaViewOutputOptionsData { Width = 8, Height = 8 });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
    }

    [Test]
    public async Task NodeBenchmarkOfRenderFrameMeasuresFullTaskCyclesTest()
    {
        // The engine's per-activity benchmark pass (what every worker runs at startup) must reach the
        // controller's own measurement — not the base adapter's rate-1.0 placeholder that would make the
        // grid allocator treat every node as equal. Every iteration is a complete task cycle (a fresh
        // process per frame), so the rate reflects what this node achieves on real tasks.
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(1), WarmupIterations = 1 };

        var result = await WitEngineNodeSdk.Instance.RunBenchmark("ParaView.RenderFrame", options);

        Assert.Multiple(() =>
        {
            Assert.That(result.Unit, Is.EqualTo(ParaViewBenchmark.UNIT));
            Assert.That(result.DatasetId, Is.EqualTo(ParaViewBenchmark.DATASET_ID));
            Assert.That(result.Iterations, Is.EqualTo(ParaViewBenchmark.MIN_CYCLES), "a tiny target still measures the minimum cycles");
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(result.Custom?[ParaViewBenchmark.CUSTOM_RENDER_WINDOW], Is.EqualTo("FakeOffscreenWindow"));
            Assert.That(result.Custom?[ParaViewBenchmark.CUSTOM_CYCLES], Is.EqualTo(ParaViewBenchmark.MIN_CYCLES.ToString()));
        });
    }

    [Test]
    public async Task NodeBenchmarkOfRenderFrameBatchMeasuresTheBatchShapeTest()
    {
        // The batch activity has its own measured rate: cycles of one process rendering several
        // frames, named by the batch dataset so the allocator never mixes it with the v3 rate.
        var options = new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(1), WarmupIterations = 1 };

        var result = await WitEngineNodeSdk.Instance.RunBenchmark("ParaView.RenderFrameBatch", options);

        Assert.Multiple(() =>
        {
            Assert.That(result.Unit, Is.EqualTo(ParaViewBenchmark.UNIT));
            Assert.That(result.DatasetId, Is.EqualTo(ParaViewBenchmark.BATCH_DATASET_ID));
            Assert.That(result.Iterations, Is.EqualTo(ParaViewBenchmark.MIN_CYCLES));
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.Custom?[ParaViewBenchmark.CUSTOM_FRAMES_PER_CYCLE], Is.EqualTo(ParaViewBenchmark.BATCH_CYCLE_FRAMES.ToString()));
        });
    }

    [Test]
    public async Task OtherParaViewActivitiesKeepTheDefaultBenchmarkTest()
    {
        // Planning, validation and assembly run on the host; only the render activities are distributed,
        // so only they need a measured rate. The others must still answer the benchmark pass without failing.
        foreach (var activity in WitEngineNodeSdk.Instance.RegisteredActivities.Where(me => me.StartsWith("ParaView.", StringComparison.Ordinal) && me != "ParaView.RenderFrame" && me != "ParaView.RenderFrameBatch"))
        {
            var result = await WitEngineNodeSdk.Instance.RunBenchmark(activity, (WitBenchmarkOptions)WitBenchmarkOptions.Default);
            Assert.That(result.Rate, Is.GreaterThan(0), $"{activity} must report a schedulable rate");
        }
    }

    #endregion

    #region Tools

    private ParaViewPackageBuilder NewPackage()
    {
        return new ParaViewPackageBuilder(Path.Combine(m_root, "pkg_" + Guid.NewGuid().ToString("N")), m_blobs);
    }

    private string Script(string fileName)
    {
        return File.ReadAllText(Path.Combine(m_scriptsPath, fileName));
    }

    #endregion
}
