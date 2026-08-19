using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Controller.Visualization.ParaView.Tests.Utils;
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

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        var report = job.Variables["report"].Value as ParaViewValidationReportData;
        var tasks = job.Variables["tasks"].Value as IReadOnlyList<ParaViewRenderTaskData?>;
        var rendered = job.Variables["rendered"].Value as IReadOnlyList<ParaViewRenderResultData?>;
        var result = job.Variables["result"].Value as IReadOnlyList<Guid?>;

        Assert.Multiple(() =>
        {
            Assert.That(report, Is.Not.Null);
            Assert.That(report!.IsValid, Is.True, string.Join("; ", report.Errors));
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

        // Download accounting: every series piece was requested exactly once (by its own task), the
        // static mesh once per task, the state once by Validate plus once per task — nothing else.
        var requests = m_blobs.Requests.GroupBy(me => me).ToDictionary(me => me.Key, me => me.Count());
        Assert.Multiple(() =>
        {
            Assert.That(requests[package.BlobOf("data/field_0.vtu")], Is.EqualTo(1));
            Assert.That(requests[package.BlobOf("data/field_1.vtu")], Is.EqualTo(1));
            Assert.That(requests[package.BlobOf("data/field_2.vtu")], Is.EqualTo(1));
            Assert.That(requests[package.BlobOf("data/mesh.vtu")], Is.EqualTo(3));
            Assert.That(requests[scene.StateBlobId], Is.EqualTo(4));
            Assert.That(requests.Keys, Is.SubsetOf(new[] { scene.StateBlobId }.Concat(package.Attachments.Select(me => me.BlobId))));
        });

        foreach (var task in tasks!.Select(me => me!))
            Assert.That(task.Attachments.Select(me => me.LogicalPath), Is.EquivalentTo(new[] { "data/mesh.vtu", $"data/field_{task.TimestepIndex}.vtu" }));
    }

    [Test]
    public async Task StillJobReturnsOneBlobTest()
    {
        var package = NewPackage().AddFile("data/field.vtu", "<VTKFile/>");
        var scene = package.BuildScene(ParaViewStateBuilder.Typical("data/field.vtu").WithTimesteps(0, 1).Build());
        var options = new ParaViewOutputOptionsData { Width = 20, Height = 10, Format = ParaViewImageFormat.Jpeg, Frames = new ParaViewFrameSelectionData { Mode = ParaViewFrameSelectionMode.Single, First = 1 } };

        var job = m_engine.Compile(Script("RenderParaViewStill.wit"));
        var status = await m_engine.ScheduleAndWaitAsync(job, scene, options);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

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

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

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
