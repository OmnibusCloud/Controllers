using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Host-side coverage for the persistent-batch split path: <c>Render.SplitBatched</c> (frames) and
/// <c>Render.SplitTilesBatched</c> (tiles) produce <c>RenderTaskBatchCollection</c> chunks of the
/// per-engine size, and the batch result shape flattens through <c>Render.Collect</c> unchanged. No
/// Blender is required (frame split is pure; tile split is given an explicit resolution).
/// </summary>
[TestFixture]
public sealed class RenderSplitBatchedTests
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
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_split_batched_test_{Guid.NewGuid():N}");
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

    #region Parse Tests

    [Test]
    public void ParseRenderSplitBatchedActivityTest()
    {
        var script = """
                     Job:TestJob(Blob:scene, Int:start, Int:end, RenderOptions:opts)
                     {
                         RenderTaskBatchCollection:tasks = Render.SplitBatched(scene, start, end, opts);
                     }
                     """;

        var job = m_engine.Compile(script);
        Assert.That(job.Activities.Count, Is.EqualTo(1));
    }

    [Test]
    public void ParseRenderSplitTilesBatchedActivityTest()
    {
        var script = """
                     Job:TestJob(Blob:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:opts, TileOptions:tileOpts)
                     {
                         RenderTaskBatchCollection:tasks = Render.SplitTilesBatched(scene, frame, tilesX, tilesY, opts, tileOpts);
                     }
                     """;

        var job = m_engine.Compile(script);
        Assert.That(job.Activities.Count, Is.EqualTo(1));
    }

    #endregion

    #region SplitBatched (Frames) Tests

    [Test]
    public async Task SplitBatchedCyclesProducesManySmallChunksTest()
    {
        // Cycles: chunk = clamp(ceil(100/48),1,8) = 3.
        var batches = await RunSplitBatchedAsync(RenderEngine.Cycles, startFrame: 1, endFrame: 100, batchSize: 0);

        AssertCoversFramesContiguously(batches, 1, 100);
        Assert.That(batches.All(b => b!.Tasks.Count <= 3), Is.True, "Cycles chunks must be <= 3");
        Assert.That(batches.Count, Is.EqualTo(34)); // 33*3 + 1
    }

    [Test]
    public async Task SplitBatchedEeveeProducesFewLargeChunksTest()
    {
        // Eevee: chunk = clamp(ceil(100/8),1,48) = 13.
        var batches = await RunSplitBatchedAsync(RenderEngine.Eevee, startFrame: 1, endFrame: 100, batchSize: 0);

        AssertCoversFramesContiguously(batches, 1, 100);
        Assert.That(batches.All(b => b!.Tasks.Count <= 13), Is.True, "Eevee chunks must be <= 13");
        Assert.That(batches.Count, Is.EqualTo(8)); // 7*13 + 9
    }

    [Test]
    public async Task SplitBatchedHoistsSceneAndOptionsOntoChunkTest()
    {
        var sceneId = Guid.NewGuid();
        var batches = await RunSplitBatchedAsync(RenderEngine.Cycles, 1, 6, 0, sceneId);

        foreach (var batch in batches)
        {
            Assert.That(batch!.SceneBlobId, Is.EqualTo(sceneId));
            Assert.That(batch.Options.Engine, Is.EqualTo(RenderEngine.Cycles));
            Assert.That(batch.Options.Samples, Is.EqualTo(64));
            foreach (var task in batch.Tasks)
                Assert.That(task.SceneBlobId, Is.EqualTo(sceneId));
        }
    }

    [Test]
    public async Task SplitBatchedRespectsBatchSizeOverrideTest()
    {
        // BatchSize override wins over the per-engine heuristic.
        var batches = await RunSplitBatchedAsync(RenderEngine.Cycles, 1, 100, batchSize: 10);

        AssertCoversFramesContiguously(batches, 1, 100);
        Assert.That(batches.Count, Is.EqualTo(10));
        Assert.That(batches.All(b => b!.Tasks.Count == 10), Is.True);
    }

    [Test]
    public async Task SplitBatchedSingleFrameProducesSingleChunkTest()
    {
        var batches = await RunSplitBatchedAsync(RenderEngine.Cycles, 42, 42, 0);
        Assert.That(batches.Count, Is.EqualTo(1));
        Assert.That(batches[0]!.Tasks.Count, Is.EqualTo(1));
        Assert.That(batches[0]!.Tasks[0].Frame, Is.EqualTo(42));
    }

    [Test]
    public async Task SplitBatchedInvalidRangeFailsTest()
    {
        var script = SplitBatchedScript();
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job, Guid.NewGuid(), 10, 5, CreateOptions(RenderEngine.Cycles, 0));
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
    }

    #endregion

    #region SplitTilesBatched (Tiles) Tests

    [Test]
    public async Task SplitTilesBatchedChunksTileGridTest()
    {
        var script = """
                     Job:Tiled(Blob:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderTaskBatchCollection:tasks = Render.SplitTilesBatched(scene, frame, tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var sceneId = Guid.NewGuid();
        // 4x4 = 16 tiles, Cycles chunk = clamp(ceil(16/48),1,8) = 1 -> 16 chunks of one tile.
        var status = await m_engine.ScheduleAndWaitAsync(
            job, [sceneId, 1, 4, 4, CreateOptions(RenderEngine.Cycles, 0, 256, 256), CreateTileOptions()]);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var batches = job.Variables["tasks"].Value as IReadOnlyList<RenderTaskBatchData?>;
        Assert.That(batches, Is.Not.Null);
        var totalTiles = batches!.Sum(b => b!.Tasks.Count);
        Assert.That(totalTiles, Is.EqualTo(16));
        Assert.That(batches.SelectMany(b => b!.Tasks).Any(t => !t.IsFullFrame), Is.True, "tile tasks must not be full-frame");
    }

    [Test]
    public async Task SplitTilesBatchedEeveeGroupsTilesIntoFewChunksTest()
    {
        var script = """
                     Job:Tiled(Blob:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderTaskBatchCollection:tasks = Render.SplitTilesBatched(scene, frame, tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        // 4x4 = 16 tiles, Eevee chunk = clamp(ceil(16/8),1,48) = 2 -> 8 chunks of two tiles.
        var status = await m_engine.ScheduleAndWaitAsync(
            job, [Guid.NewGuid(), 1, 4, 4, CreateOptions(RenderEngine.Eevee, 0, 256, 256), CreateTileOptions()]);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var batches = job.Variables["tasks"].Value as IReadOnlyList<RenderTaskBatchData?>;
        Assert.That(batches, Is.Not.Null);
        Assert.That(batches!.Sum(b => b!.Tasks.Count), Is.EqualTo(16));
        Assert.That(batches.All(b => b!.Tasks.Count <= 2), Is.True);
    }

    #endregion

    #region Flatten-Through-Collect Tests

    [Test]
    public async Task CollectFlattensBatchResultShapeTest()
    {
        // Render.Collect must accept the batch result shape (RenderResultBatchCollection) and flatten it.
        var script = """
                     Job:CollectBatched(RenderResultBatchCollection:rendered, RenderOptions:options)
                     {
                         BlobCollection:result = Render.Collect(rendered, options);
                     }
                     """;

        var job = m_engine.Compile(script);

        var blob0 = Guid.NewGuid();
        var blob1 = Guid.NewGuid();
        var blob2 = Guid.NewGuid();
        var blob3 = Guid.NewGuid();

        // Two batches, frames intentionally out of order across batches to exercise the sort.
        var batches = new List<RenderResultBatchData?>
        {
            new() { Results = [new RenderResultData { Index = 2, ImageBlobId = blob2 }, new RenderResultData { Index = 3, ImageBlobId = blob3 }] },
            new() { Results = [new RenderResultData { Index = 0, ImageBlobId = blob0 }, new RenderResultData { Index = 1, ImageBlobId = blob1 }] }
        };

        var status = await m_engine.ScheduleAndWaitAsync(job, batches, new RenderOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var collected = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(collected, Is.Not.Null);
        Assert.That(collected!.Count, Is.EqualTo(4));
        Assert.That(collected[0], Is.EqualTo(blob0));
        Assert.That(collected[1], Is.EqualTo(blob1));
        Assert.That(collected[2], Is.EqualTo(blob2));
        Assert.That(collected[3], Is.EqualTo(blob3));
    }

    #endregion

    #region Tools

    private async Task<IReadOnlyList<RenderTaskBatchData?>> RunSplitBatchedAsync(
        RenderEngine engine, int startFrame, int endFrame, int batchSize, Guid? sceneId = null)
    {
        var job = m_engine.Compile(SplitBatchedScript());
        var status = await m_engine.ScheduleAndWaitAsync(
            job, sceneId ?? Guid.NewGuid(), startFrame, endFrame, CreateOptions(engine, batchSize));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var batches = job.Variables["tasks"].Value as IReadOnlyList<RenderTaskBatchData?>;
        Assert.That(batches, Is.Not.Null);
        return batches!;
    }

    private static void AssertCoversFramesContiguously(IReadOnlyList<RenderTaskBatchData?> batches, int start, int end)
    {
        var frames = batches.SelectMany(b => b!.Tasks).Select(t => t.Frame).ToList();
        Assert.That(frames.Count, Is.EqualTo(end - start + 1));
        Assert.That(frames, Is.EqualTo(Enumerable.Range(start, end - start + 1)).AsCollection);

        var taskIndices = batches.SelectMany(b => b!.Tasks).Select(t => t.TaskIndex).ToList();
        Assert.That(taskIndices, Is.EqualTo(Enumerable.Range(0, frames.Count)).AsCollection);
    }

    private static string SplitBatchedScript()
    {
        return """
               Job:SplitBatchedTest(Blob:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
               {
                   RenderTaskBatchCollection:tasks = Render.SplitBatched(scene, startFrame, endFrame, options);
               }
               """;
    }

    private static RenderOptionsData CreateOptions(RenderEngine engine, int batchSize, int resX = 1920, int resY = 1080)
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = engine,
            Samples = 64,
            ResolutionX = resX,
            ResolutionY = resY,
            BatchSize = batchSize
        };
    }

    private static TileOptionsData CreateTileOptions()
    {
        return new TileOptionsData { OverlapPx = 0, BlendMode = TileBlendMode.CenterPriorityCrop };
    }

    #endregion
}
