using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Basic bundled-script rendering tests (still/frames/tiled/video) running the
/// Render.* bundled .wit scripts end-to-end against the standard test scene
/// with real Blender. Excludes cube_diorama and scene-render variants which
/// live in sibling fixtures.
/// </summary>
[TestFixture]
internal sealed class RenderProductionScriptBundledRenderTests : RenderProductionScriptBlenderTestsBase
{
    #region Tests

    [Test]
    public async Task BundledRenderStillScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStill.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);
        Assert.That(File.Exists(m_blobService.GetStoredPath(blobId!.Value)), Is.True);
        Assert.That(new FileInfo(m_blobService.GetStoredPath(blobId.Value)).Length, Is.GreaterThan(0));

        RenderGoldenFileAssert.AssertImageMatches(
            m_blobService.GetStoredPath(blobId.Value),
            m_solutionRoot!,
            "RenderStill",
            RenderEngine.Cycles,
            64,
            64);
    }

    [Test]
    public async Task BundledRenderStillScriptRealRunProducesNonBlackImageTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStill.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);

        AssertImageIsNotSolidBlack(storedPath, "Bundled RenderStill frame 1");
    }

    [TestCase("RenderStillCycles.wit", RenderEngine.Cycles)]
    [TestCase("RenderStillEevee.wit", RenderEngine.Eevee)]
    [TestCase("RenderStillGreasePencil.wit", RenderEngine.GreasePencil)]
    public async Task BundledEngineSpecificRenderStillScriptRealRunProducesNonBlackImageTest(string scriptFileName, RenderEngine engine)
    {
        var benchmarkStillScenePath = RenderTestAssetPaths.GetBenchmarkStillScenePath(m_solutionRoot!);
        if (!File.Exists(benchmarkStillScenePath))
            Assert.Ignore($"Benchmark still scene not found at {benchmarkStillScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, scriptFileName));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(benchmarkStillScenePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            CreateOptions(engine));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        AssertImageIsNotSolidBlack(storedPath, $"{Path.GetFileNameWithoutExtension(scriptFileName)} frame 1");
    }

    [Test]
    public async Task BundledRenderFramesScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderFrames.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobs = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(blobs, Is.Not.Null);
        Assert.That(blobs!, Has.Count.EqualTo(3));

        foreach (var blobId in blobs)
        {
            Assert.That(blobId, Is.Not.Null);
            var storedPath = m_blobService.GetStoredPath(blobId!.Value);
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        }
    }

    [TestCase("RenderFramesCycles.wit", RenderEngine.Cycles)]
    [TestCase("RenderFramesEevee.wit", RenderEngine.Eevee)]
    [TestCase("RenderFramesGreasePencil.wit", RenderEngine.GreasePencil)]
    public async Task BundledEngineSpecificRenderFramesScriptRealRunProducesExpectedFrameSetTest(string scriptFileName, RenderEngine engine)
    {
        var benchmarkStillScenePath = RenderTestAssetPaths.GetBenchmarkStillScenePath(m_solutionRoot!);
        if (!File.Exists(benchmarkStillScenePath))
            Assert.Ignore($"Benchmark still scene not found at {benchmarkStillScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, scriptFileName));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(benchmarkStillScenePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions(engine));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var blobs = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(blobs, Is.Not.Null);
        Assert.That(blobs!, Has.Count.EqualTo(3));

        var storedPaths = new List<string>(blobs.Count);
        foreach (var blobId in blobs)
        {
            Assert.That(blobId, Is.Not.Null);
            var storedPath = m_blobService.GetStoredPath(blobId!.Value);
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
            storedPaths.Add(storedPath);
        }

        AssertImageIsNotSolidBlack(storedPaths[0], $"{Path.GetFileNameWithoutExtension(scriptFileName)} frame 1");
    }

    [Test]
    public async Task BundledRenderStillTiledScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStillTiled.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(),
            CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        RenderGoldenFileAssert.AssertImageMatches(storedPath, m_solutionRoot!, "RenderStillTiled", RenderEngine.Cycles, 64, 64);
    }

    [Test]
    public async Task BundledRenderStillTiledOverlapScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStillTiled.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        RenderGoldenFileAssert.AssertImageMatches(storedPath, m_solutionRoot!, "RenderStillTiledOverlap", RenderEngine.Cycles, 64, 64);
    }

    [TestCase("RenderStillTiledCycles.wit", RenderEngine.Cycles)]
    [TestCase("RenderStillTiledEevee.wit", RenderEngine.Eevee)]
    [TestCase("RenderStillTiledGreasePencil.wit", RenderEngine.GreasePencil)]
    public async Task BundledEngineSpecificRenderStillTiledScriptRealRunProducesNonBlackImageTest(string scriptFileName, RenderEngine engine)
    {
        var benchmarkStillScenePath = RenderTestAssetPaths.GetBenchmarkStillScenePath(m_solutionRoot!);
        if (!File.Exists(benchmarkStillScenePath))
            Assert.Ignore($"Benchmark still scene not found at {benchmarkStillScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, scriptFileName));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(benchmarkStillScenePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(engine),
            CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        AssertImageIsNotSolidBlack(storedPath, $"{Path.GetFileNameWithoutExtension(scriptFileName)} frame 1");
    }

    [Test]
    public async Task BundledRenderVideoScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderVideo.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions(),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var videoBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(videoBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(videoBlobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(storedPath, Does.EndWith(".mp4"));
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        // Video output: the upstream exists + non-zero-length checks are
        // sufficient — SHA-256 compare of an mp4 was never reliable across
        // Blender/ffmpeg builds, and there is no perceptual mp4 differ here.
    }

    [TestCase("RenderVideoCycles.wit", RenderEngine.Cycles)]
    [TestCase("RenderVideoEevee.wit", RenderEngine.Eevee)]
    [TestCase("RenderVideoGreasePencil.wit", RenderEngine.GreasePencil)]
    public async Task BundledEngineSpecificRenderVideoScriptRealRunProducesVideoBlobTest(string scriptFileName, RenderEngine engine)
    {
        var benchmarkVideoScenePath = RenderTestAssetPaths.GetBenchmarkVideoScenePath(m_solutionRoot!);
        if (!File.Exists(benchmarkVideoScenePath))
            Assert.Ignore($"Benchmark video scene not found at {benchmarkVideoScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, scriptFileName));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(benchmarkVideoScenePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions(engine),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var videoBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(videoBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(videoBlobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(storedPath, Does.EndWith(".mp4"));
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
    }

    #endregion
}
