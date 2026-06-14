using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Dcc.Tests.Mock;
using OutWit.Controller.Render.Dcc.Tests.Utils;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Tests.Activities;

[TestFixture]
public sealed class RenderBuildBlendFromDccSceneDeformationTests
{
    #region Fields

    private RenderTestBlobService m_blobService = null!;
    private string m_storageDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_buildblend_deformation_dcc_test_{Guid.NewGuid():N}");
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
    public async Task BuildBlendFromDccSceneWithDeformationFramesRendersDifferentGeometryPerFrameTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc deformation still-render integration test.");

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
                     Job:BuildAndRenderDeformedStill(DccScene:scene, Int:frame, RenderOptions:options)
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
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();

        // Frame 1 = rest pose (base positions); frame 2 = the same mesh scaled 2x about the origin
        // (a clearly different silhouette), baked as a deformation cache.
        var mesh = scene.Meshes[0];
        mesh.DeformationFrames =
        [
            new DccMeshDeformationFrameData
            {
                Frame = 1,
                Positions = [.. mesh.Positions.Select(me => new DccVector3Data { X = me.X, Y = me.Y, Z = me.Z })]
            },
            new DccMeshDeformationFrameData
            {
                Frame = 2,
                Positions = [.. mesh.Positions.Select(me => new DccVector3Data { X = me.X * 2d, Y = me.Y * 2d, Z = me.Z * 2d })]
            }
        ];

        var restBytes = await RenderFrameAsync(hostEngine, job, scene, 1);
        var deformedBytes = await RenderFrameAsync(hostEngine, job, scene, 2);

        Assert.Multiple(() =>
        {
            Assert.That(restBytes.Length, Is.GreaterThan(0));
            Assert.That(deformedBytes.Length, Is.GreaterThan(0));
            // Different baked geometry at the two frames must produce different renders.
            Assert.That(restBytes.SequenceEqual(deformedBytes), Is.False, "Frame 1 (rest) and frame 2 (deformed) rendered identically - deformation was not applied.");
        });
    }

    #endregion

    #region Tools

    private async Task<byte[]> RenderFrameAsync(IWitEngine hostEngine, IWitJob job, DccSceneData scene, int frame)
    {
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateRenderOptions());
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed at frame {frame}: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        return await File.ReadAllBytesAsync(storedPath);
    }

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
