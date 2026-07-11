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
    public void UnzipRejectsPayloadsOverTheDecompressedSceneLimitTest()
    {
        // Gzip expands ~1000:1 at the format's limit — a small crafted payload must not be able
        // to pin multi-GB buffers on the worker. A gzipped stream of zeros compresses tiny but
        // inflates past the cap.
        using var bombStream = new MemoryStream();
        using (var gzip = new GZipStream(bombStream, CompressionMode.Compress, leaveOpen: true))
        {
            var zeros = new byte[1024 * 1024];
            var chunks = (WitActivityAdapterRenderUnzipDccScene.MAX_DECOMPRESSED_SCENE_BYTES / zeros.Length) + 2;
            for (var i = 0L; i < chunks; i++)
                gzip.Write(zeros, 0, zeros.Length);
        }

        var exception = Assert.Throws<InvalidOperationException>(
            () => WitActivityAdapterRenderUnzipDccScene.Unzip(bombStream.ToArray()));
        Assert.That(exception!.Message, Does.Contain("decompressed scene limit"));
    }

    [Test]
    public void EveryTypeReachableFromTheScenePayloadIsVersionTolerantTest()
    {
        // The scene payload crosses the plugin↔server boundary with INDEPENDENT release cadences.
        // MemoryPack's default object format hard-fails on payloads carrying unknown members, so
        // any non-VersionTolerant type nested in DccSceneData bricks old-server submissions the
        // moment a newer client appends a field (RenderSceneAttachmentRefData did exactly that).
        var pending = new Queue<Type>();
        pending.Enqueue(typeof(DccSceneData));
        var seen = new HashSet<Type>();
        var offenders = new List<string>();

        while (pending.Count > 0)
        {
            var type = pending.Dequeue();

            if (type.IsArray)
                type = type.GetElementType()!;
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                    pending.Enqueue(argument);
                type = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
            }

            if (!seen.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string) || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                continue;

            var packable = type.GetCustomAttributes(inherit: false)
                .FirstOrDefault(me => me.GetType().Name == "MemoryPackableAttribute");
            if (packable is not null)
            {
                var generateType = packable.GetType().GetProperty("GenerateType")?.GetValue(packable)?.ToString();
                if (generateType != "VersionTolerant")
                    offenders.Add($"{type.FullName} ({generateType})");
            }

            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                pending.Enqueue(property.PropertyType);
        }

        Assert.That(offenders, Is.Empty,
            "every MemoryPackable type reachable from DccSceneData must be GenerateType.VersionTolerant");
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
