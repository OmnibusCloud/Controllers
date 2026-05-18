using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class RenderFrameEngineSpecificProcessingTests
{
    #region Fields

    private string m_blobStoragePath = null!;
    private RenderTestBlobService m_blobService = null!;
    private string? m_benchmarkStillScenePath;
    private string? m_controllersPath;
    private string? m_solutionRoot;
    private IWitEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (m_solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(m_solutionRoot);
        if (blenderDir == null)
            Assert.Ignore("No supported Blender prerequisites for current OS/architecture");

        if (!new BlenderRunner(blenderDir, NullLogger.Instance).IsAvailable)
            Assert.Ignore($"Blender not found at {blenderDir}");

        m_controllersPath = RenderTestAssetPaths.FindControllersPath();
        if (m_controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_benchmarkStillScenePath = RenderTestAssetPaths.GetBenchmarkStillScenePath(m_solutionRoot);
        if (!File.Exists(m_benchmarkStillScenePath))
            Assert.Ignore($"Benchmark still scene not found at {m_benchmarkStillScenePath}");
    }

    [SetUp]
    public void SetUp()
    {
        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_render_frame_engine_test_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_blobStoragePath);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: m_controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: m_controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderTestNodesManager(WitEngineNodeSdk.Instance));
            });
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Tests

    [TestCase("Render.Frame.Cycles", RenderEngine.Cycles)]
    [TestCase("Render.Frame.Eevee", RenderEngine.Eevee)]
    [TestCase("Render.Frame.GreasePencil", RenderEngine.GreasePencil)]
    public async Task DirectEngineSpecificRenderFrameActivityRealRunTest(string activityName, RenderEngine engine)
    {
        var script =
            "Job:Direct(RenderTask:task)\n" +
            "{\n" +
            $"    RenderResult:result = {activityName}(task);\n" +
            "}";

        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_benchmarkStillScenePath!);
        var task = CreateRenderTask(sceneBlobId, CreateOptions(engine));

        var status = await m_engine.ScheduleAndWaitAsync(job, task);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var result = job.Variables["result"].Value as RenderResultData;
        Assert.That(result, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result!.Index, Is.EqualTo(task.TaskIndex));
            Assert.That(File.Exists(m_blobService.GetStoredPath(result.ImageBlobId)), Is.True);
            Assert.That(new FileInfo(m_blobService.GetStoredPath(result.ImageBlobId)).Length, Is.GreaterThan(0));
        });
    }

    [TestCase(RenderEngine.Cycles)]
    [TestCase(RenderEngine.Eevee)]
    [TestCase(RenderEngine.GreasePencil)]
    public async Task LegacyRenderFrameActivityRemainsEngineAgnosticRealRunTest(RenderEngine engine)
    {
        const string script = "Job:Direct(RenderTask:task)\n{\n    RenderResult:result = Render.Frame(task);\n}";

        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_benchmarkStillScenePath!);
        var task = CreateRenderTask(sceneBlobId, CreateOptions(engine));

        var status = await m_engine.ScheduleAndWaitAsync(job, task);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var result = job.Variables["result"].Value as RenderResultData;
        Assert.That(result, Is.Not.Null);
        Assert.That(File.Exists(m_blobService.GetStoredPath(result!.ImageBlobId)), Is.True);
    }

    [TestCase("Render.Frame.Cycles", RenderEngine.Eevee, "Cycles")]
    [TestCase("Render.Frame.Eevee", RenderEngine.GreasePencil, "Eevee")]
    [TestCase("Render.Frame.GreasePencil", RenderEngine.Cycles, "GreasePencil")]
    public async Task DirectEngineSpecificRenderFrameActivityFailsForWrongEngineTest(string activityName, RenderEngine actualEngine, string expectedEngineName)
    {
        var script =
            "Job:Direct(RenderTask:task)\n" +
            "{\n" +
            $"    RenderResult:result = {activityName}(task);\n" +
            "}";

        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_benchmarkStillScenePath!);
        var task = CreateRenderTask(sceneBlobId, CreateOptions(actualEngine));

        var status = await m_engine.ScheduleAndWaitAsync(job, task);

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(status.Message, Does.Contain($"RenderOptions.Engine={expectedEngineName}"));
        });
    }

    #endregion

    #region Tools

    private static RenderTaskData CreateRenderTask(Guid sceneBlobId, RenderOptionsData options)
    {
        return new RenderTaskData
        {
            SceneBlobId = sceneBlobId,
            Frame = 1,
            TaskIndex = 0,
            Options = options
        };
    }

    private static RenderOptionsData CreateOptions(RenderEngine engine)
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = engine,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };
    }

    #endregion
}
