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
        // THE farm incident: Blender returned 983x693 for a tile our boundary grid sizes at 983x694.
        var task = CreateFarmIncidentTask();
        var renderedPath = await WritePngAsync(983, 693, new Rgba32(200, 90, 40, 255));

        var normalized = await RenderFrameOutputHelper.NormalizeRenderedTileOutputAsync(
            m_runner, NullLogger.Instance, "Render.Frame", renderedPath, task, m_testDir, CancellationToken.None);

        Assert.That(normalized.UseLogicalTileBounds, Is.False);
        Assert.That(normalized.RenderedPath, Is.Not.EqualTo(renderedPath), "a re-anchored copy is expected");

        var info = await m_runner.GetImageInfoAsync(normalized.RenderedPath);
        Assert.That((info.Width, info.Height), Is.EqualTo((983, 694)));

        // The padded bottom row is edge-smeared, not black: the seam-safe guarantee for zero overlap.
        using var image = await Image.LoadAsync<Rgba32>(normalized.RenderedPath);
        Assert.That(image[500, 693].R, Is.EqualTo(200).Within(2));
        Assert.That(image[500, 693].A, Is.EqualTo(255));
    }

    [Test]
    public async Task OversizedTileWithinToleranceIsCroppedToTheBoundaryGridTest()
    {
        // The mirror case: a build that snaps an interior edge UP returns a 1px-too-big tile.
        var task = CreateFarmIncidentTask();
        var renderedPath = await WritePngAsync(984, 695, new Rgba32(60, 160, 60, 255));

        var normalized = await RenderFrameOutputHelper.NormalizeRenderedTileOutputAsync(
            m_runner, NullLogger.Instance, "Render.Frame", renderedPath, task, m_testDir, CancellationToken.None);

        var info = await m_runner.GetImageInfoAsync(normalized.RenderedPath);
        Assert.That((info.Width, info.Height), Is.EqualTo((983, 694)));
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

    private async Task<string> WritePngAsync(int width, int height, Rgba32 color)
    {
        var path = Path.Combine(m_testDir, $"tile_{width}x{height}_{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(width, height, color);
        await image.SaveAsPngAsync(path);
        return path;
    }

    #endregion
}
