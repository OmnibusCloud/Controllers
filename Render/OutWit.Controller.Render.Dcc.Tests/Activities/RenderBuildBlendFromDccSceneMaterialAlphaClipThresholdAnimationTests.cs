using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Dcc.Tests.Mock;
using OutWit.Controller.Render.Dcc.Tests.Utils;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Tests.Activities;

[TestFixture]
public sealed class RenderBuildBlendFromDccSceneMaterialAlphaClipThresholdAnimationTests
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
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_buildblend_material_alpha_clip_threshold_animation_dcc_test_{Guid.NewGuid():N}");
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
    public async Task BuildBlendFromDccSceneWithMaterialAlphaClipThresholdAnimationThenRenderFramesCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc material-alpha-clip-threshold frame-render integration test.");

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
                     Job:BuildAndRenderMaterialAlphaClipThresholdAnimatedFrames(DccScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         BlobCollection:result = Render.Collect(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Clip;
        scene.ImageAssets.Add(DccRenderTestData.CreateOpacityImageAsset());
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Opacity,
            ImageAssetId = "image:opacity"
        });
        var keyframe1 = DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(1, 0.5d);
        keyframe1.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var keyframe2 = DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(2, 0.2d);
        keyframe2.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Materials[0].AlphaClipThresholdKeyframes = [keyframe1, keyframe2];
        scene.RenderSettings.FrameEnd = 2;

        var albedoPath = Path.Combine(m_storageDir, "albedo.png");
        var opacityPath = Path.Combine(m_storageDir, "opacity.png");
        var pngBytes = Convert.FromBase64String(MINIMAL_PNG_BASE64);
        File.WriteAllBytes(albedoPath, pngBytes);
        File.WriteAllBytes(opacityPath, pngBytes);

        var albedoBlobId = m_blobService.RegisterExistingFile(albedoPath);
        var opacityBlobId = m_blobService.RegisterExistingFile(opacityPath);
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(albedoBlobId));
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(opacityBlobId, "C:/textures/opacity.png", "textures/opacity.png"));

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, 2, CreateRenderOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobIds = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(resultBlobIds, Is.Not.Null);
        Assert.That(resultBlobIds!, Has.Count.EqualTo(2));

        foreach (var resultBlobId in resultBlobIds)
        {
            Assert.That(resultBlobId, Is.Not.Null);

            var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(storedPath), Is.True);
                Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
                Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
            });
        }
    }

    [Test]
    public async Task BuildBlendFromDccSceneWithMaterialAlphaClipThresholdAnimationThenRenderVideoCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc material-alpha-clip-threshold video-render integration test.");

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
                     Job:BuildAndRenderMaterialAlphaClipThresholdAnimatedVideo(DccScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options, VideoOptions:video)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         BlobCollection:frames = Render.Collect(rendered, options);
                         Blob:result = Render.EncodeVideo(frames, video);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Materials[0].AlphaMode = DccMaterialAlphaMode.Clip;
        scene.ImageAssets.Add(DccRenderTestData.CreateOpacityImageAsset());
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Opacity,
            ImageAssetId = "image:opacity"
        });
        var keyframe1 = DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(1, 0.5d);
        keyframe1.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var keyframe2 = DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(2, 0.2d);
        keyframe2.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var keyframe3 = DccRenderTestData.CreateMaterialAlphaClipThresholdKeyframe(3, 0.7d);
        keyframe3.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        scene.Materials[0].AlphaClipThresholdKeyframes = [keyframe1, keyframe2, keyframe3];
        scene.RenderSettings.FrameEnd = 3;

        var albedoPath = Path.Combine(m_storageDir, "albedo.png");
        var opacityPath = Path.Combine(m_storageDir, "opacity.png");
        var pngBytes = Convert.FromBase64String(MINIMAL_PNG_BASE64);
        File.WriteAllBytes(albedoPath, pngBytes);
        File.WriteAllBytes(opacityPath, pngBytes);

        var albedoBlobId = m_blobService.RegisterExistingFile(albedoPath);
        var opacityBlobId = m_blobService.RegisterExistingFile(opacityPath);
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(albedoBlobId));
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(opacityBlobId, "C:/textures/opacity.png", "textures/opacity.png"));

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, 3, CreateRenderOptions(), CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".mp4"));
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

    private static VideoOptionsData CreateVideoOptions()
    {
        return new VideoOptionsData
        {
            FrameRate = 24,
            ConstantRateFactor = 23
        };
    }

    #endregion
}
