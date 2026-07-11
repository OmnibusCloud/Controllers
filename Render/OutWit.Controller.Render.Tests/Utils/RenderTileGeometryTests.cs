using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Pins the boundary-first pixel mapping (RenderTileGeometry) and the Blender float-truncation
/// replica. The 1950×1372 case is the 2026-07-11 farm incident: a healthy bottom tile was rejected
/// with "expected 983x694 but got 983x693" because the expected size was a rounded WIDTH while
/// Blender snaps each BOUNDARY by float32 truncation.
/// </summary>
[TestFixture]
public sealed class RenderTileGeometryTests
{
    #region Boundary / Span

    [Test]
    public void SpansTileEveryAxisWithNoGapsOrOverlapsTest()
    {
        // Sum of spans over a grid == the axis length, for dimensions that do NOT divide evenly.
        foreach (var dimension in new[] { 8, 977, 1000, 1372, 1950 })
        {
            foreach (var tiles in new[] { 2, 3, 7 })
            {
                var total = 0;
                for (var index = 0; index < tiles; index++)
                {
                    var min = index / (float)tiles;
                    var max = (index + 1) / (float)tiles;
                    total += RenderTileGeometry.Span(min, max, dimension);

                    // Contiguity: this tile ends exactly where the next one starts.
                    Assert.That(
                        RenderTileGeometry.BoundaryPixel(min, dimension) + RenderTileGeometry.Span(min, max, dimension),
                        Is.EqualTo(RenderTileGeometry.BoundaryPixel(max, dimension)),
                        $"tile {index}/{tiles} over {dimension}");
                }

                Assert.That(total, Is.EqualTo(dimension), $"{tiles} tiles over {dimension} must cover the axis exactly");
            }
        }
    }

    [Test]
    public void SpanDisagreesWithARoundedWidthAtFractionalBoundariesTest()
    {
        // The class of bug this geometry exists to prevent: round((max-min)·dim) says 2,
        // the boundary difference says 1 — sizes MUST come from boundaries, not widths.
        const float min = 0.5f;
        const float max = 1f;
        const int dimension = 3;

        var roundedWidth = (int)Math.Round((max - min) * dimension, MidpointRounding.AwayFromZero);
        var boundarySpan = RenderTileGeometry.Span(min, max, dimension);

        Assert.That(roundedWidth, Is.EqualTo(2));
        Assert.That(boundarySpan, Is.EqualTo(1));
        Assert.That(RenderTileGeometry.BoundaryPixel(min, dimension) + boundarySpan, Is.EqualTo(dimension));
    }

    #endregion

    #region Blender snapping (farm incident 2026-07-11)

    [Test]
    public void FarmIncidentTileGeometryIsReproducedTest()
    {
        // 1950×1372, 2×2 tiles, 8px overlap — bottom-left tile (task 0).
        const int width = 1950;
        const int height = 1372;
        var maxX = 0.5f + 8f / width;   // effective render bounds as planned by RenderTileTaskBuilder
        var maxY = 0.5f + 8f / height;

        // Our rounded boundaries say 983×694…
        Assert.That(RenderTileGeometry.Span(0f, maxX, width), Is.EqualTo(983));
        Assert.That(RenderTileGeometry.Span(0f, maxY, height), Is.EqualTo(694));

        // …Blender's float32 truncation produced 983×693 — exactly the farm error message.
        Assert.That(RenderTileGeometry.BlenderSpan(0f, maxX, width), Is.EqualTo(983));
        Assert.That(RenderTileGeometry.BlenderSpan(0f, maxY, height), Is.EqualTo(693));
    }

    [Test]
    public void BlenderBoundaryNeverExceedsTheRoundedBoundaryTest()
    {
        // Truncation is a floor for non-negative products: 0 <= round - trunc <= 1. The snap
        // normalizer relies on this (it only ever crops at min edges and pads at max edges).
        foreach (var dimension in new[] { 8, 693, 977, 1080, 1372, 1950, 4096 })
        {
            for (var numerator = 0; numerator <= 64; numerator++)
            {
                var fraction = numerator / 64f;
                var rounded = RenderTileGeometry.BoundaryPixel(fraction, dimension);
                var truncated = RenderTileGeometry.BlenderBoundaryPixel(fraction, dimension);

                Assert.That(rounded - truncated, Is.InRange(0, 1), $"fraction {fraction} over {dimension}");
            }
        }
    }

    [Test]
    public void ExactFrameEdgesNeverSnapTest()
    {
        // 0 and 1 are exactly representable — deltas at frame edges are always zero, which is why
        // the snap normalizer's pad/crop pixels always land in overlap margins the stitch discards.
        foreach (var dimension in new[] { 693, 1372, 1950 })
        {
            Assert.That(RenderTileGeometry.BoundaryPixel(0f, dimension), Is.EqualTo(RenderTileGeometry.BlenderBoundaryPixel(0f, dimension)));
            Assert.That(RenderTileGeometry.BoundaryPixel(1f, dimension), Is.EqualTo(RenderTileGeometry.BlenderBoundaryPixel(1f, dimension)));
        }
    }

    #endregion
}
