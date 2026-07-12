using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Tile-output normalization against the real ffmpeg: exact tiles pass through, Blender-snapped
/// tiles (float-truncated boundaries, the 2026-07-11 farm incident) are re-anchored to the rounded
/// boundary grid, and anything else still fails loudly.
/// </summary>
[TestFixture]
public sealed class RenderFrameOutputHelperTests
{
    #region Fields

    private string m_testDir = null!;
    private FfmpegRunner m_runner = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witcloud_tile_norm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var ffmpegDir = Path.Combine(solutionRoot, "@Prerequisites", "ffmpeg");
        m_runner = new FfmpegRunner(ffmpegDir, NullLogger.Instance);
        if (!m_runner.IsAvailable)
            Assert.Ignore($"ffmpeg not found at {ffmpegDir}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_testDir))
            Directory.Delete(m_testDir, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task ExactBoundarySizedTilePassesThroughUntouchedTest()
    {
        var task = CreateFarmIncidentTask();
        var renderedPath = await WritePngAsync(983, 694, new Rgba32(40, 90, 200, 255));

        var normalized = await RenderFrameOutputHelper.NormalizeRenderedTileOutputAsync(
            m_runner, NullLogger.Instance, "Render.Frame", renderedPath, task, m_testDir, CancellationToken.None);

        Assert.That(normalized.RenderedPath, Is.EqualTo(renderedPath));
        Assert.That(normalized.UseLogicalTileBounds, Is.False);
    }

    [Test]
    public async Task BlenderSnappedTileIsReanchoredToTheBoundaryGridTest()
    {
        // THE farm incident: Blender truncated the interior TOP boundary (max-Y) of the bottom-left
        // tile, returning 983x693 for a boundary grid that sizes it 983x694 — the tile is missing its
        // TOP row. A marker on the input's top row lets us prove the re-anchor pads at the TOP (so the
        // retained content lands one row lower, at its true grid position), not the bottom.
        var task = CreateFarmIncidentTask();
        var marker = new Rgba32(0, 200, 0, 255);
        var body = new Rgba32(200, 90, 40, 255);
        var renderedPath = await WriteTopMarkedPngAsync(983, 693, marker, body);

        var normalized = await RenderFrameOutputHelper.NormalizeRenderedTileOutputAsync(
            m_runner, NullLogger.Instance, "Render.Frame", renderedPath, task, m_testDir, CancellationToken.None);

        Assert.That(normalized.UseLogicalTileBounds, Is.False);
        Assert.That(normalized.RenderedPath, Is.Not.EqualTo(renderedPath), "a re-anchored copy is expected");

        var info = await m_runner.GetImageInfoAsync(normalized.RenderedPath);
        Assert.That((info.Width, info.Height), Is.EqualTo((983, 694)));

        using var image = await Image.LoadAsync<Rgba32>(normalized.RenderedPath);
        Assert.Multiple(() =>
        {
            // The pad landed on top: the marker row shifted down to row 1, the smeared pad row 0 copies
            // it (green, not black — the zero-overlap seam-safe guarantee), and the body follows below.
            Assert.That(image[500, 0].G, Is.EqualTo(200).Within(4), "top pad row must be edge-smeared, not black");
            Assert.That(image[500, 1].G, Is.EqualTo(200).Within(4), "the marker row must shift down to row 1 (content padded at top)");
            Assert.That(image[500, 2].R, Is.EqualTo(200).Within(4), "the body must follow the shifted marker");
            Assert.That(image[500, 2].G, Is.EqualTo(90).Within(6), "row 2 is body (G=90), not the green marker (G=200)");
        });
    }

    [Test]
    public async Task OversizedTileFromMinBoundarySnapIsCroppedAtTheLeadingEdgeTest()
    {
        // The mirror class: when Blender truncates a MIN (bottom) interior boundary, it renders one
        // row BELOW the rounded grid — a 1px-too-tall tile whose extra row is at the BOTTOM. The
        // re-anchor must crop that bottom row and keep the top, so a marker on the input's top row
        // survives at output row 0 (a right/bottom-only scheme that cropped the wrong end would drop it).
        var task = CreateBottomBoundarySnapTask();
        var marker = new Rgba32(0, 200, 0, 255);
        var body = new Rgba32(60, 120, 200, 255);
        var renderedPath = await WriteTopMarkedPngAsync(100, 679, marker, body);

        var normalized = await RenderFrameOutputHelper.NormalizeRenderedTileOutputAsync(
            m_runner, NullLogger.Instance, "Render.Frame", renderedPath, task, m_testDir, CancellationToken.None);

        var info = await m_runner.GetImageInfoAsync(normalized.RenderedPath);
        Assert.That((info.Width, info.Height), Is.EqualTo((100, 678)));

        using var image = await Image.LoadAsync<Rgba32>(normalized.RenderedPath);
        Assert.Multiple(() =>
        {
            Assert.That(image[50, 0].G, Is.EqualTo(200).Within(4), "the top marker row must be retained (bottom row cropped)");
            Assert.That(image[50, 1].B, Is.EqualTo(200).Within(4), "the body must follow the retained marker");
        });
    }

    [Test]
    public void GeometryErrorsBeyondTheSnapToleranceStillFailLoudlyTest()
    {
        var task = CreateFarmIncidentTask();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var renderedPath = await WritePngAsync(983, 600, new Rgba32(10, 10, 10, 255));
            await RenderFrameOutputHelper.NormalizeRenderedTileOutputAsync(
                m_runner, NullLogger.Instance, "Render.Frame", renderedPath, task, m_testDir, CancellationToken.None);
        });
    }

    [Test]
    public void ImpossibleSnapSizeForTheTaskGeometryFailsLoudlyTest()
    {
        // 984x695 cannot arise from the farm task: its X boundaries are frame edges (never snap) and
        // its max-Y boundary only truncates DOWN. The model-based re-anchor must reject it rather than
        // blindly crop the right/bottom (the old ±2 tolerance masked geometry errors as valid tiles).
        var task = CreateFarmIncidentTask();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var renderedPath = await WritePngAsync(984, 695, new Rgba32(60, 160, 60, 255));
            await RenderFrameOutputHelper.NormalizeRenderedTileOutputAsync(
                m_runner, NullLogger.Instance, "Render.Frame", renderedPath, task, m_testDir, CancellationToken.None);
        });
    }

    #endregion

    #region Tools

    /// <summary>Bottom-left tile of the 1950×1372 / 2×2 / 8px-overlap grid from the farm incident.</summary>
    private static RenderTaskData CreateFarmIncidentTask()
    {
        const int width = 1950;
        const int height = 1372;

        return new RenderTaskData
        {
            Frame = 1,
            TaskIndex = 0,
            TileMinX = 0f,
            TileMaxX = 0.5f,
            TileMinY = 0f,
            TileMaxY = 0.5f,
            RenderMinX = 0f,
            RenderMaxX = 0.5f + 8f / width,
            RenderMinY = 0f,
            RenderMaxY = 0.5f + 8f / height,
            Options = new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                ResolutionX = width,
                ResolutionY = height
            }
        };
    }

    /// <summary>
    /// A tile whose interior boundary is the BOTTOM (min-Y) edge and truncates like the farm value,
    /// so Blender renders one row below the rounded grid (100×679 for a grid that sizes it 100×678).
    /// X is a full, frame-aligned span so only the Y min boundary snaps.
    /// </summary>
    private static RenderTaskData CreateBottomBoundarySnapTask()
    {
        const int width = 100;
        const int height = 1372;

        return new RenderTaskData
        {
            Frame = 1,
            TaskIndex = 0,
            TileMinX = 0f,
            TileMaxX = 1f,
            TileMinY = 0.5f,
            TileMaxY = 1f,
            RenderMinX = 0f,
            RenderMaxX = 1f,
            RenderMinY = 0.5f + 8f / height, // the same fraction the farm build truncated (694/1372 -> 693)
            RenderMaxY = 1f,
            Options = new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                ResolutionX = width,
                ResolutionY = height
            }
        };
    }

    private async Task<string> WritePngAsync(int width, int height, Rgba32 color)
    {
        var path = Path.Combine(m_testDir, $"tile_{width}x{height}_{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(width, height, color);
        await image.SaveAsPngAsync(path);
        return path;
    }

    /// <summary>Writes a PNG whose top row is <paramref name="marker"/> and every other row is <paramref name="body"/>.</summary>
    private async Task<string> WriteTopMarkedPngAsync(int width, int height, Rgba32 marker, Rgba32 body)
    {
        var path = Path.Combine(m_testDir, $"tile_marked_{width}x{height}_{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(width, height, body);
        for (var x = 0; x < width; x++)
            image[x, 0] = marker;
        await image.SaveAsPngAsync(path);
        return path;
    }

    #endregion
}
