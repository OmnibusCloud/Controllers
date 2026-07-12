using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Pins the job-sized input guards: a legitimate render stays inside every ceiling, and a hostile
/// or malformed submission is rejected before the host allocates a task list or pixel plane sized by
/// the offending number.
/// </summary>
[TestFixture]
public sealed class RenderInputLimitsTests
{
    #region Frame range

    [Test]
    public void ValidateFrameRangeAcceptsARealisticAnimationTest()
    {
        Assert.DoesNotThrow(() => RenderInputLimits.ValidateFrameRange(1, 2500));
    }

    [Test]
    public void ValidateFrameRangeAcceptsExactlyTheLimitTest()
    {
        Assert.DoesNotThrow(() => RenderInputLimits.ValidateFrameRange(0, RenderInputLimits.MAX_FRAMES_PER_SPLIT - 1));
    }

    [Test]
    public void ValidateFrameRangeRejectsOneOverTheLimitTest()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RenderInputLimits.ValidateFrameRange(0, RenderInputLimits.MAX_FRAMES_PER_SPLIT));

        Assert.That(exception!.Message, Does.Contain("frame"));
    }

    [Test]
    public void ValidateFrameRangeRejectsAnIntMaxRangeWithoutOverflowingTest()
    {
        // The pathological OOM vector: a range near int.MaxValue would materialize 2^31 tasks.
        // Computed as a long so the count itself does not overflow to a small/negative number.
        var exception = Assert.Throws<InvalidOperationException>(
            () => RenderInputLimits.ValidateFrameRange(int.MinValue, int.MaxValue));

        Assert.That(exception!.Message, Does.Contain("frame"));
    }

    #endregion

    #region Tile grid

    [Test]
    public void ValidateTileGridAcceptsATypicalGridTest()
    {
        Assert.DoesNotThrow(() => RenderInputLimits.ValidateTileGrid(8, 8));
    }

    [Test]
    public void ValidateTileGridRejectsAnEnormousGridWithoutOverflowingTest()
    {
        // tilesX * tilesY as int would overflow; the guard multiplies as long and rejects it.
        var exception = Assert.Throws<InvalidOperationException>(
            () => RenderInputLimits.ValidateTileGrid(100_000, 100_000));

        Assert.That(exception!.Message, Does.Contain("tile"));
    }

    #endregion

    #region AlphaBlend resolution

    [Test]
    public void ValidateAlphaBlendResolutionAccepts8KTest()
    {
        Assert.DoesNotThrow(() => RenderInputLimits.ValidateAlphaBlendResolution(7680, 4320));
    }

    [Test]
    public void ValidateAlphaBlendResolutionRejectsAHugeCanvasWithoutOverflowingTest()
    {
        // 50000 * 50000 = 2.5e9 overflows int (and would allocate ~110 GB of double planes); the
        // guard computes the pixel count as long and rejects it, pointing at CenterPriorityCrop.
        var exception = Assert.Throws<InvalidOperationException>(
            () => RenderInputLimits.ValidateAlphaBlendResolution(50_000, 50_000));

        Assert.That(exception!.Message, Does.Contain("CenterPriorityCrop"));
    }

    #endregion
}
