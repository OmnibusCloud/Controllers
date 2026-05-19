using Microsoft.Extensions.DependencyInjection;
using System.Text;
using OutWit.Controller.Render.Dcc.Tests.Mock;
using OutWit.Controller.Render.Dcc.Tests.Utils;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Tests.Activities;

[TestFixture]
public sealed class RenderBuildBlendFromDccSceneTests
{
    #region Fields

    private RenderTestBlobService m_blobService = null!;
    private IWitEngine m_engine = null!;
    private string m_storageDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_buildblend_dcc_test_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_storageDir);

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_storageDir))
            Directory.Delete(m_storageDir, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void ParseRenderBuildBlendFromDccSceneActivityTest()
    {
        var script = """
                     Job:Build(DccScene:scene)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                     }
                     """;

        var job = m_engine.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["blend"], Is.Not.Null);
    }

    [Test]
    public void ParseRenderClearSceneActivityTest()
    {
        var script = """
                     Job:Build(DccScene:scene)
                     {
                         Render.ClearScene(scene);
                     }
                     """;

        var job = m_engine.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task BuildBlendFromDccSceneRejectsMissingSceneNameTest()
    {
        var script = """
                     Job:Build(DccScene:scene)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.SceneName = string.Empty;

        var status = await m_engine.ScheduleAndWaitAsync(job, scene);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("SceneName"));
    }

    [Test]
    public async Task BuildBlendFromDccSceneRejectsMissingMeshReferenceTest()
    {
        var script = """
                     Job:Build(DccScene:scene)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Nodes[0].MeshId = "mesh:missing";

        var status = await m_engine.ScheduleAndWaitAsync(job, scene);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("missing mesh"));
    }

    [Test]
    public async Task BuildBlendFromDccSceneBuildsBlendAndUploadsBlobTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc build test.");

        var script = """
                     Job:Build(DccScene:scene)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        var texturePath = Path.Combine(m_storageDir, "albedo.png");
        File.WriteAllBytes(texturePath, Convert.FromBase64String(MINIMAL_PNG_BASE64));
        var textureBlobId = m_blobService.RegisterExistingFile(texturePath);
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(textureBlobId));

        var status = await m_engine.ScheduleAndWaitAsync(job, scene);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var blobId = (Guid?)job.Variables["blend"].Value;
        Assert.That(blobId, Is.Not.Null);
        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".blend"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
            Assert.That(job.Variables.Any(me => me.Name == "scene"), Is.True,
                "Render.BuildBlendFromDccScene should not remove the source scene implicitly.");
        });
    }

    [Test]
    public async Task ClearSceneRemovesSceneVariableTest()
    {
        var script = """
                     Job:Build(DccScene:scene)
                     {
                         Render.ClearScene(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();

        var status = await m_engine.ScheduleAndWaitAsync(job, scene);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        Assert.That(job.Variables.Any(me => me.Name == "scene"), Is.False,
            "Render.ClearScene should remove the host-only source scene variable explicitly.");
    }

    #endregion

    #region Constants

    private const string MINIMAL_PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    #endregion
}
