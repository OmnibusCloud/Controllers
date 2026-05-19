using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Dcc.Tests.Mock;
using OutWit.Controller.Render.Dcc.Tests.Utils;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Tests.Activities;

[TestFixture]
public sealed class RenderBuildBlendFromDccSceneCameraClipAnimationTests
{
    #region Fields

    private RenderTestBlobService m_blobService = null!;
    private string m_storageDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_buildblend_camera_clip_animation_dcc_test_{Guid.NewGuid():N}");
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
    public async Task BuildBlendFromDccSceneWithCameraClipAnimationThenRenderFramesCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc camera-clip frame-render integration test.");

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
                     Job:BuildAndRenderCameraClipAnimatedFrames(DccScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
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
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        var camera = DccRenderTestData.CreateCamera();
        var nearKeyframe1 = DccRenderTestData.CreateCameraNearClipKeyframe(1, 0.1d);
        nearKeyframe1.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var nearKeyframe2 = DccRenderTestData.CreateCameraNearClipKeyframe(2, 0.5d);
        nearKeyframe2.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var farKeyframe1 = DccRenderTestData.CreateCameraFarClipKeyframe(1, 500d);
        farKeyframe1.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var farKeyframe2 = DccRenderTestData.CreateCameraFarClipKeyframe(2, 600d);
        farKeyframe2.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        camera.NearClipKeyframes = [nearKeyframe1, nearKeyframe2];
        camera.FarClipKeyframes = [farKeyframe1, farKeyframe2];
        scene.Cameras.Add(camera);
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.RenderSettings.FrameEnd = 2;

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
    public async Task BuildBlendFromDccSceneWithCameraClipAnimationThenRenderVideoCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc camera-clip video-render integration test.");

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
                     Job:BuildAndRenderCameraClipAnimatedVideo(DccScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options, VideoOptions:video)
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
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        var camera = DccRenderTestData.CreateCamera();
        var nearKeyframe1 = DccRenderTestData.CreateCameraNearClipKeyframe(1, 0.1d);
        nearKeyframe1.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var nearKeyframe2 = DccRenderTestData.CreateCameraNearClipKeyframe(2, 0.5d);
        nearKeyframe2.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var nearKeyframe3 = DccRenderTestData.CreateCameraNearClipKeyframe(3, 0.2d);
        nearKeyframe3.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var farKeyframe1 = DccRenderTestData.CreateCameraFarClipKeyframe(1, 500d);
        farKeyframe1.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var farKeyframe2 = DccRenderTestData.CreateCameraFarClipKeyframe(2, 600d);
        farKeyframe2.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        var farKeyframe3 = DccRenderTestData.CreateCameraFarClipKeyframe(3, 450d);
        farKeyframe3.InterpolationMode = DccKeyframeInterpolationMode.Linear;
        camera.NearClipKeyframes = [nearKeyframe1, nearKeyframe2, nearKeyframe3];
        camera.FarClipKeyframes = [farKeyframe1, farKeyframe2, farKeyframe3];
        scene.Cameras.Add(camera);
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.RenderSettings.FrameEnd = 3;

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
