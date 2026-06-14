using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Dcc.Tests.Mock;
using OutWit.Controller.Render.Dcc.Tests.Utils;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Tests.Activities;

[TestFixture]
public sealed class RenderBuildBlendFromDccSceneDisplacementTests
{
    #region Constants

    private const string MINIMAL_PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    #endregion

    #region Fields

    private RenderTestBlobService m_blobService = null!;
    private string m_storageDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_buildblend_displacement_dcc_test_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_storageDir);

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
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
    public async Task BuildBlendFromDccSceneWithDisplacementMapThenRenderStillCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc displacement still-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRenderDisplacedStill(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);

        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        // Replace the material's textures with a single displacement map (bound to material:cube).
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();

        var dispPath = Path.Combine(m_storageDir, "displacement.png");
        File.WriteAllBytes(dispPath, Convert.FromBase64String(MINIMAL_PNG_BASE64));
        var dispBlobId = m_blobService.RegisterExistingFile(dispPath);

        scene.ImageAssets.Add(new DccImageAssetData
        {
            Id = "image:displacement",
            Name = "Displacement",
            SourcePath = "C:/textures/displacement.png",
            RelativePath = "textures/displacement.png",
            AssetKind = "ImageAsset"
        });
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(dispBlobId, "C:/textures/displacement.png", "textures/displacement.png"));
        scene.Materials[0].DisplacementScale = 0.2d;
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Displacement,
            ImageAssetId = "image:displacement"
        });

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions());

        // The .blend with a Displacement node + subdivision modifier must build and render on Blender 5.1.
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    #endregion

    #region Helpers

    private static RenderOptionsData CreateRenderOptions()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64,
            Denoise = false
        };
    }

    #endregion
}
