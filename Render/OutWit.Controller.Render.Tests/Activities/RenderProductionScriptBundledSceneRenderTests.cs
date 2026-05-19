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
/// Scene-based bundled-script rendering tests — the Render.*Scene*.wit
/// variants that take an inline RenderSceneData (large via blob, small via
/// inline bytes) for still/frames/tiled/video output.
/// </summary>
[TestFixture]
internal sealed class RenderProductionScriptBundledSceneRenderTests : RenderProductionScriptBlenderTestsBase
{
    #region Tests

    [Test]
    public async Task BundledRenderSceneFramesScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneFrames.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            3,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobs = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(blobs, Is.Not.Null);
        Assert.That(blobs!, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task BundledRenderSceneFramesLargeScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneFramesLarge.wit"));
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
    }

    [Test]
    public async Task BundledRenderSceneStillScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStill.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);
        Assert.That(File.Exists(m_blobService.GetStoredPath(blobId!.Value)), Is.True);
    }

    [Test]
    public async Task BundledRenderSceneStillLargeScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillLarge.wit"));
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
    }

    [Test]
    public async Task BundledRenderSceneStillTiledScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillTiled.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            2,
            2,
            CreateOptions(width: 256, height: 256),
            CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        RenderGoldenFileAssert.AssertImageMatches(storedPath, m_solutionRoot!, "RenderSceneStillTiled", RenderEngine.Cycles, 256, 256);
    }

    [Test]
    public async Task BundledRenderSceneStillTiledOverlapScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillTiled.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            2,
            2,
            CreateOptions(width: 256, height: 256),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        RenderGoldenFileAssert.AssertImageMatches(storedPath, m_solutionRoot!, "RenderSceneStillTiledOverlap", RenderEngine.Cycles, 256, 256);
    }

    [Test]
    public async Task BundledRenderSceneStillTiledLargeScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillTiledLarge.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(width: 256, height: 256),
            CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        RenderGoldenFileAssert.AssertImageMatches(storedPath, m_solutionRoot!, "RenderSceneStillTiledLarge", RenderEngine.Cycles, 256, 256);
    }

    [Test]
    public async Task BundledRenderSceneStillTiledLargeOverlapScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillTiledLarge.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(width: 256, height: 256),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        RenderGoldenFileAssert.AssertImageMatches(storedPath, m_solutionRoot!, "RenderSceneStillTiledLargeOverlap", RenderEngine.Cycles, 256, 256);
    }

    [Test]
    public async Task BundledRenderSceneVideoScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneVideo.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            3,
            CreateOptions(),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var videoBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(videoBlobId, Is.Not.Null);
    }

    [Test]
    public async Task BundledRenderSceneVideoLargeScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneVideoLarge.wit"));
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
    }

    #endregion
}
