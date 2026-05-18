using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class RenderTiledTests
{
    #region Constants

    private const string TINY_PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAVSURBVBhXY/jPwABC/xkYGhj+gwAARk8JeKKlzvcAAAAASUVORK5CYII=";
    private const string SIX_BY_SIX_PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAYAAAAGCAYAAADgzO9IAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAUSURBVBhXY/jPwPAfG2ZAF6CnBAAEuUe5FQOetAAAAABJRU5ErkJggg==";
    #endregion

    #region Fields

    private RenderTestBlobService m_blobService = null!;
    private IWitEngine m_engine = null!;
    private string m_storageDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_tiles_test_{Guid.NewGuid():N}");
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
    public void ParseRenderSplitTilesActivityTest()
    {
        var script = """
                     Job:Tiled(Blob:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderTaskCollection:tasks = Render.SplitTiles(scene, frame, tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        Assert.That(job.Activities.Count, Is.EqualTo(1));
    }

    [Test]
    public void ParseRenderCollectTilesActivityTest()
    {
        var script = """
                     Job:Tiled(RenderResultCollection:results, RenderOptions:options, TileOptions:tileOptions)
                     {
                         Blob:result = Render.CollectTiles(results, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        Assert.That(job.Activities.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task SplitTilesGeneratesExpectedTaskCountTest()
    {
        var script = """
                     Job:Tiled(Blob:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderTaskCollection:tasks = Render.SplitTiles(scene, frame, tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var sceneBlobId = Guid.NewGuid();
        var status = await m_engine.ScheduleAndWaitAsync(job, [sceneBlobId, 1, 2, 3, CreateOptions(), CreateTileOptionsData()]);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var tasks = job.Variables["tasks"].Value as IReadOnlyList<RenderTaskData?>;
        Assert.That(tasks, Is.Not.Null);
        Assert.That(tasks!, Has.Count.EqualTo(6));
        Assert.That(tasks.Any(me => me is { IsFullFrame: false }), Is.True);
    }

    [Test]
    public async Task CollectTilesProducesBlobTest()
    {
        var script = """
                     Job:Tiled(RenderResultCollection:results, RenderOptions:options, TileOptions:tileOptions)
                     {
                         Blob:result = Render.CollectTiles(results, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var tileBlobIds = new List<RenderResultData?>();
        for (var index = 0; index < 4; index++)
        {
            var tilePath = Path.Combine(m_storageDir, $"tile_{index + 1:D4}.png");
            File.WriteAllBytes(tilePath, Convert.FromBase64String(TINY_PNG_BASE64));
            var blobId = m_blobService.RegisterExistingFile(tilePath);

            var x = index % 2;
            var y = index / 2;
            tileBlobIds.Add(new RenderResultData
            {
                Index = index,
                ImageBlobId = blobId,
                TileMinX = x / 2f,
                TileMaxX = (x + 1) / 2f,
                TileMinY = y / 2f,
                TileMaxY = (y + 1) / 2f
            });
        }

        var status = await m_engine.ScheduleAndWaitAsync(job, tileBlobIds, CreateTileOptions(), CreateTileOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var outputBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(outputBlobId, Is.Not.Null);
        Assert.That(File.Exists(m_blobService.GetStoredPath(outputBlobId!.Value)), Is.True);
    }

    [Test]
    public async Task CollectTilesInfersOutputResolutionWhenNotSpecifiedTest()
    {
        var script = """
                     Job:Tiled(RenderResultCollection:results, RenderOptions:options, TileOptions:tileOptions)
                     {
                         Blob:result = Render.CollectTiles(results, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var tileBlobIds = new List<RenderResultData?>();
        for (var index = 0; index < 4; index++)
        {
            var tilePath = Path.Combine(m_storageDir, $"tile_infer_{index + 1:D4}.png");
            File.WriteAllBytes(tilePath, Convert.FromBase64String(TINY_PNG_BASE64));
            var blobId = m_blobService.RegisterExistingFile(tilePath);

            var x = index % 2;
            var y = index / 2;
            tileBlobIds.Add(new RenderResultData
            {
                Index = index,
                ImageBlobId = blobId,
                TileMinX = x / 2f,
                TileMaxX = (x + 1) / 2f,
                TileMinY = y / 2f,
                TileMaxY = (y + 1) / 2f
            });
        }

        var status = await m_engine.ScheduleAndWaitAsync(job, tileBlobIds, new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 0,
            ResolutionY = 0
        }, CreateTileOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var outputBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(outputBlobId, Is.Not.Null);
        Assert.That(File.Exists(m_blobService.GetStoredPath(outputBlobId!.Value)), Is.True);
    }

    [Test]
    public async Task CollectTilesCenterPriorityCropWithOverlapProducesExpectedDimensionsAndQuadrantsTest()
    {
        var script = """
                     Job:Tiled(RenderResultCollection:results, RenderOptions:options, TileOptions:tileOptions)
                     {
                         Blob:result = Render.CollectTiles(results, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var tileResults = new List<RenderResultData?>
        {
            CreateQuadrantTileResult(0, 0f, 0.5f, 0f, 0.5f, 0f, 0.75f, 0f, 0.75f, new Rgba32(0, 0, 255, 255), 0, 2),
            CreateQuadrantTileResult(1, 0.5f, 1f, 0f, 0.5f, 0.25f, 1f, 0f, 0.75f, new Rgba32(255, 255, 0, 255), 2, 2),
            CreateQuadrantTileResult(2, 0f, 0.5f, 0.5f, 1f, 0f, 0.75f, 0.25f, 1f, new Rgba32(255, 0, 0, 255), 0, 0),
            CreateQuadrantTileResult(3, 0.5f, 1f, 0.5f, 1f, 0.25f, 1f, 0.25f, 1f, new Rgba32(0, 255, 0, 255), 2, 0)
        };

        var status = await m_engine.ScheduleAndWaitAsync(job, tileResults, CreateOverlapOptions(), CreateTileOptionsData(2, TileBlendMode.CenterPriorityCrop));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var outputBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(outputBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(outputBlobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);

        using var image = await Image.LoadAsync<Rgba32>(storedPath);
        Assert.That(image.Width, Is.EqualTo(8));
        Assert.That(image.Height, Is.EqualTo(8));
        AssertPixelClose(image[1, 1], new Rgba32(255, 0, 0, 255));
        AssertPixelClose(image[6, 1], new Rgba32(0, 255, 0, 255));
        AssertPixelClose(image[1, 6], new Rgba32(0, 0, 255, 255));
        AssertPixelClose(image[6, 6], new Rgba32(255, 255, 0, 255));
    }

    [Test]
    public async Task SplitTilesExpandsRenderBoundsForOverlapTest()
    {
        var script = """
                     Job:Tiled(Blob:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderTaskCollection:tasks = Render.SplitTiles(scene, frame, tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var sceneBlobId = Guid.NewGuid();
        var status = await m_engine.ScheduleAndWaitAsync(job, [sceneBlobId, 1, 2, 2, CreateOverlapOptions(), CreateTileOptionsData(2)]);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var tasks = job.Variables["tasks"].Value as IReadOnlyList<RenderTaskData?>;
        Assert.That(tasks, Is.Not.Null);
        Assert.That(tasks!, Has.Count.EqualTo(4));

        var first = tasks[0]!;
        Assert.That(first.TileMinX, Is.EqualTo(0f).Within(0.0001));
        Assert.That(first.TileMaxX, Is.EqualTo(0.5f).Within(0.0001));
        Assert.That(first.RenderMinX, Is.EqualTo(0f).Within(0.0001));
        Assert.That(first.RenderMaxX, Is.EqualTo(0.75f).Within(0.0001));
        Assert.That(first.RenderMaxY, Is.EqualTo(0.75f).Within(0.0001));
    }

    [Test]
    public async Task SplitTilesFailsForOverlapEqualToCoreTileSizeTest()
    {
        var script = """
                     Job:Tiled(Blob:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderTaskCollection:tasks = Render.SplitTiles(scene, frame, tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var sceneBlobId = Guid.NewGuid();
        var status = await m_engine.ScheduleAndWaitAsync(job, [sceneBlobId, 1, 2, 2, CreateOverlapOptions(), CreateTileOptionsData(4)]);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("smaller than the core tile size"));
    }

    [Test]
    public async Task CollectTilesProducesBlobWithOverlapTest()
    {
        var script = """
                     Job:Tiled(RenderResultCollection:results, RenderOptions:options, TileOptions:tileOptions)
                     {
                         Blob:result = Render.CollectTiles(results, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var tileResults = new List<RenderResultData?>();
        for (var index = 0; index < 4; index++)
        {
            var tilePath = Path.Combine(m_storageDir, $"overlap_{index + 1:D4}.png");
            File.WriteAllBytes(tilePath, Convert.FromBase64String(SIX_BY_SIX_PNG_BASE64));
            var blobId = m_blobService.RegisterExistingFile(tilePath);

            var x = index % 2;
            var y = index / 2;
            tileResults.Add(new RenderResultData
            {
                Index = index,
                ImageBlobId = blobId,
                TileMinX = x / 2f,
                TileMaxX = (x + 1) / 2f,
                TileMinY = y / 2f,
                TileMaxY = (y + 1) / 2f,
                RenderMinX = Math.Max(0f, x / 2f - 0.25f),
                RenderMaxX = Math.Min(1f, (x + 1) / 2f + 0.25f),
                RenderMinY = Math.Max(0f, y / 2f - 0.25f),
                RenderMaxY = Math.Min(1f, (y + 1) / 2f + 0.25f)
            });
        }

        var status = await m_engine.ScheduleAndWaitAsync(job, tileResults, CreateOverlapOptions(), CreateTileOptionsData(2, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var outputBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(outputBlobId, Is.Not.Null);
        Assert.That(File.Exists(m_blobService.GetStoredPath(outputBlobId!.Value)), Is.True);
    }

    [Test]
    public async Task CollectTilesFailsForMissingCoverageTest()
    {
        var script = """
                     Job:Tiled(RenderResultCollection:results, RenderOptions:options, TileOptions:tileOptions)
                     {
                         Blob:result = Render.CollectTiles(results, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var tileResults = new List<RenderResultData?>();
        for (var index = 0; index < 3; index++)
        {
            var tilePath = Path.Combine(m_storageDir, $"missing_{index + 1:D4}.png");
            File.WriteAllBytes(tilePath, Convert.FromBase64String(TINY_PNG_BASE64));
            var blobId = m_blobService.RegisterExistingFile(tilePath);

            var x = index % 2;
            var y = index / 2;
            tileResults.Add(new RenderResultData
            {
                Index = index,
                ImageBlobId = blobId,
                TileMinX = x / 2f,
                TileMaxX = (x + 1) / 2f,
                TileMinY = y / 2f,
                TileMaxY = (y + 1) / 2f
            });
        }

        var status = await m_engine.ScheduleAndWaitAsync(job, tileResults, CreateTileOptions(), CreateTileOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("complete rectangular tile grid"));
    }

    [Test]
    public async Task CollectTilesFailsForUnexpectedTileDimensionsTest()
    {
        var script = """
                     Job:Tiled(RenderResultCollection:results, RenderOptions:options, TileOptions:tileOptions)
                     {
                         Blob:result = Render.CollectTiles(results, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var tileResults = new List<RenderResultData?>();
        for (var index = 0; index < 4; index++)
        {
            var tilePath = Path.Combine(m_storageDir, $"mismatch_{index + 1:D4}.png");
            File.WriteAllBytes(tilePath, Convert.FromBase64String(TINY_PNG_BASE64));
            var blobId = m_blobService.RegisterExistingFile(tilePath);

            var x = index % 2;
            var y = index / 2;
            tileResults.Add(new RenderResultData
            {
                Index = index,
                ImageBlobId = blobId,
                TileMinX = x / 2f,
                TileMaxX = (x + 1) / 2f,
                TileMinY = y / 2f,
                TileMaxY = (y + 1) / 2f
            });
        }

        var mismatchedOptions = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 8,
            ResolutionY = 8
        };

        var status = await m_engine.ScheduleAndWaitAsync(job, tileResults, mismatchedOptions, CreateTileOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("tile size mismatch"));
    }

    [Test]
    public async Task CollectTilesFailsWhenRenderedBoundsDoNotContainLogicalTileBoundsTest()
    {
        var script = """
                     Job:Tiled(RenderResultCollection:results, RenderOptions:options, TileOptions:tileOptions)
                     {
                         Blob:result = Render.CollectTiles(results, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var tileResults = new List<RenderResultData?>();
        for (var index = 0; index < 4; index++)
        {
            var tilePath = Path.Combine(m_storageDir, $"invalid_bounds_{index + 1:D4}.png");
            File.WriteAllBytes(tilePath, Convert.FromBase64String(TINY_PNG_BASE64));
            var blobId = m_blobService.RegisterExistingFile(tilePath);

            var x = index % 2;
            var y = index / 2;
            tileResults.Add(new RenderResultData
            {
                Index = index,
                ImageBlobId = blobId,
                TileMinX = x / 2f,
                TileMaxX = (x + 1) / 2f,
                TileMinY = y / 2f,
                TileMaxY = (y + 1) / 2f,
                RenderMinX = x == 0 ? 0.25f : x / 2f,
                RenderMaxX = (x + 1) / 2f,
                RenderMinY = y / 2f,
                RenderMaxY = (y + 1) / 2f
            });
        }

        var status = await m_engine.ScheduleAndWaitAsync(job, tileResults, CreateTileOptions(), CreateTileOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
        Assert.That(status.Message, Does.Contain("rendered bounds to fully contain the logical tile bounds"));
    }

    #endregion

    #region Tools

    private static RenderOptionsData CreateOptions()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };
    }

    private static RenderOptionsData CreateTileOptions()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 4,
            ResolutionY = 4
        };
    }

    private static RenderOptionsData CreateOverlapOptions()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 8,
            ResolutionY = 8
        };
    }

    private static TileOptionsData CreateTileOptionsData(int overlapPx = 0, TileBlendMode blendMode = TileBlendMode.CenterPriorityCrop)
    {
        return new TileOptionsData
        {
            OverlapPx = overlapPx,
            BlendMode = blendMode
        };
    }

    private RenderResultData CreateQuadrantTileResult(
        int index,
        float tileMinX,
        float tileMaxX,
        float tileMinY,
        float tileMaxY,
        float renderMinX,
        float renderMaxX,
        float renderMinY,
        float renderMaxY,
        Rgba32 coreColor,
        int cropX,
        int cropY)
    {
        var tilePath = Path.Combine(m_storageDir, $"quadrant_{index + 1:D4}.png");
        using (var image = new Image<Rgba32>(6, 6, new Rgba32(0, 0, 0, 255)))
        {
            for (var y = cropY; y < cropY + 4; y++)
            {
                for (var x = cropX; x < cropX + 4; x++)
                    image[x, y] = coreColor;
            }

            image.SaveAsPng(tilePath);
        }

        var blobId = m_blobService.RegisterExistingFile(tilePath);
        return new RenderResultData
        {
            Index = index,
            ImageBlobId = blobId,
            TileMinX = tileMinX,
            TileMaxX = tileMaxX,
            TileMinY = tileMinY,
            TileMaxY = tileMaxY,
            RenderMinX = renderMinX,
            RenderMaxX = renderMaxX,
            RenderMinY = renderMinY,
            RenderMaxY = renderMaxY
        };
    }

    private static void AssertPixelClose(Rgba32 actual, Rgba32 expected)
    {
        Assert.That(Math.Abs(actual.R - expected.R), Is.LessThanOrEqualTo(2));
        Assert.That(Math.Abs(actual.G - expected.G), Is.LessThanOrEqualTo(2));
        Assert.That(Math.Abs(actual.B - expected.B), Is.LessThanOrEqualTo(2));
        Assert.That(Math.Abs(actual.A - expected.A), Is.LessThanOrEqualTo(0));
    }

    #endregion
}
