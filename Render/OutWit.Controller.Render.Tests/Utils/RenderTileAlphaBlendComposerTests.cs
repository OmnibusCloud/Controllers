using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// AlphaBlend tile compositing against the real ffmpeg: the output alpha must be the feather-weighted
/// average of the SOURCE alpha, so a semi-transparent render survives the stitch instead of being
/// forced fully opaque.
/// </summary>
[TestFixture]
public sealed class RenderTileAlphaBlendComposerTests
{
    #region Fields

    private string m_testDir = null!;
    private FfmpegRunner m_runner = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witcloud_alphablend_test_{Guid.NewGuid():N}");
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
    public async Task ComposePreservesPartialSourceAlphaTest()
    {
        // A single full-frame tile whose pixels are half-transparent (alpha 128). With no overlap the
        // feather weight is 1 everywhere, so the composited alpha must come back ~128 — the old
        // compositor divided 255·w by w and returned 255 for every covered pixel, erasing transparency.
        const int width = 8;
        const int height = 8;
        var tilePath = await WritePngAsync(width, height, new Rgba32(200, 60, 40, 128));

        var context = new RenderTileValidationContext
        {
            Result = FullFrameResult(),
            LocalPath = tilePath,
            ImageInfo = new RenderImageInfo { Width = width, Height = height, FormatName = "png" }
        };

        var composed = await RenderTileAlphaBlendComposer.ComposeAsync(
            [context], width, height, m_runner, CancellationToken.None);

        var centerIndex = (4 * width + 4) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(composed.PixelBytes[centerIndex + 3], Is.EqualTo(128).Within(2), "partial source alpha must be preserved");
            Assert.That(composed.PixelBytes[centerIndex], Is.EqualTo(200).Within(3), "colour must survive the composite");
        });
    }

    [Test]
    public async Task ComposeKeepsFullyOpaqueTileOpaqueTest()
    {
        const int width = 8;
        const int height = 8;
        var tilePath = await WritePngAsync(width, height, new Rgba32(30, 120, 210, 255));

        var context = new RenderTileValidationContext
        {
            Result = FullFrameResult(),
            LocalPath = tilePath,
            ImageInfo = new RenderImageInfo { Width = width, Height = height, FormatName = "png" }
        };

        var composed = await RenderTileAlphaBlendComposer.ComposeAsync(
            [context], width, height, m_runner, CancellationToken.None);

        var centerIndex = (4 * width + 4) * 4;
        Assert.That(composed.PixelBytes[centerIndex + 3], Is.EqualTo(255), "an opaque tile must stay opaque");
    }

    #endregion

    #region Tools

    private static RenderResultData FullFrameResult()
    {
        return new RenderResultData
        {
            Index = 0,
            TileMinX = 0f,
            TileMaxX = 1f,
            TileMinY = 0f,
            TileMaxY = 1f,
            RenderMinX = 0f,
            RenderMaxX = 1f,
            RenderMinY = 0f,
            RenderMaxY = 1f
        };
    }

    private async Task<string> WritePngAsync(int width, int height, Rgba32 color)
    {
        var path = Path.Combine(m_testDir, $"alpha_tile_{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(width, height, color);
        await image.SaveAsPngAsync(path);
        return path;
    }

    #endregion
}
