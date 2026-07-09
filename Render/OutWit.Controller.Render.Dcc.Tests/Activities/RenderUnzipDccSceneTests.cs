using System.IO.Compression;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Dcc.Adapters;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Tests.Mock;
using OutWit.Controller.Render.Dcc.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Dcc.Tests.Activities;

[TestFixture]
internal sealed class RenderUnzipDccSceneTests : RenderBuildBlendFromDccSceneTestsBase
{
    [Test]
    public void UnzipRoundTripsGzippedScenePayloadTest()
    {
        var scene = DccRenderTestData.CreateValidScene();

        var unzipped = WitActivityAdapterRenderUnzipDccScene.Unzip(Pack(scene));

        Assert.Multiple(() =>
        {
            Assert.That(unzipped.SceneName, Is.EqualTo(scene.SceneName));
            Assert.That(unzipped.Is(scene), Is.True);
        });
    }

    [Test]
    public void UnzipRejectsNonGzipPayloadTest()
    {
        // A raw (uncompressed) MemoryPack payload must fail loudly, not deserialize garbage —
        // the packed scripts only accept gzipped scenes.
        var raw = MemoryPackSerializer.Serialize(DccRenderTestData.CreateValidScene());

        Assert.Throws<InvalidDataException>(() => WitActivityAdapterRenderUnzipDccScene.Unzip(raw));
    }

    [Test]
    public async Task BundledRenderDccSceneStillPackedScriptCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for bundled RenderDccSceneStillPacked integration test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");
        var scriptPath = Path.Combine(solutionRoot, "@Scripts", "Debug", "RenderDccSceneStillPacked.wit");
        if (!File.Exists(scriptPath))
            Assert.Ignore($"Bundled script was not found at {scriptPath}");

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

        var script = await File.ReadAllTextAsync(scriptPath);
        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        // The client-side shape of the packed submission: gzipped MemoryPack bytes instead of
        // the inline DccScene parameter.
        var status = await hostEngine.ScheduleAndWaitAsync(job, Pack(scene), 1, CreateRenderOptions());

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

    private static byte[] Pack(DccSceneData scene)
    {
        var payload = MemoryPackSerializer.Serialize(scene);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(payload, 0, payload.Length);

        return output.ToArray();
    }
}
