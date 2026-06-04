using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Utils;

[TestFixture]
public class RenderChunkPolicyTests
{
    #region Render-Bound (Cycles) Tests

    [Test]
    public void CyclesUsesManySmallChunksTest()
    {
        // 240 frames / target 48 = ceil 5, within max 8 -> chunk 5 => 48 chunks.
        var chunk = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Cycles, 240, 0);
        Assert.That(chunk, Is.EqualTo(5));
    }

    [Test]
    public void CyclesClampsToMaxChunkTest()
    {
        // 480 / 48 = 10, clamped to MAX_CHUNK_RENDER_BOUND (8).
        var chunk = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Cycles, 480, 0);
        Assert.That(chunk, Is.EqualTo(RenderChunkPolicy.MAX_CHUNK_RENDER_BOUND));
        Assert.That(chunk, Is.EqualTo(8));
    }

    [Test]
    public void CyclesSmallJobGetsChunkOfOneTest()
    {
        // 10 / 48 = ceil 1 -> floor keeps it at 1 so the job still splits across nodes.
        var chunk = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Cycles, 10, 0);
        Assert.That(chunk, Is.EqualTo(1));
    }

    #endregion

    #region Overhead-Bound (Eevee / Grease Pencil) Tests

    [Test]
    public void EeveeUsesFewLargeChunksTest()
    {
        // 240 / target 8 = 30, within max 48 -> chunk 30 => 8 chunks.
        var chunk = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Eevee, 240, 0);
        Assert.That(chunk, Is.EqualTo(30));
    }

    [Test]
    public void EeveeClampsToMaxChunkTest()
    {
        // 480 / 8 = 60, clamped to MAX_CHUNK_OVERHEAD_BOUND (48).
        var chunk = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Eevee, 480, 0);
        Assert.That(chunk, Is.EqualTo(RenderChunkPolicy.MAX_CHUNK_OVERHEAD_BOUND));
        Assert.That(chunk, Is.EqualTo(48));
    }

    [Test]
    public void GreasePencilSharesOverheadBoundPolicyTest()
    {
        var eevee = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Eevee, 240, 0);
        var greasePencil = RenderChunkPolicy.ComputeChunkSize(RenderEngine.GreasePencil, 240, 0);
        Assert.That(greasePencil, Is.EqualTo(eevee));
    }

    [Test]
    public void OverheadBoundChunkAlwaysLargerThanRenderBoundForSameCountTest()
    {
        // The whole point: overhead-bound engines amortise scene-load with bigger chunks.
        var cycles = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Cycles, 120, 0);
        var eevee = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Eevee, 120, 0);
        Assert.That(eevee, Is.GreaterThan(cycles));
    }

    #endregion

    #region Override Tests

    [Test]
    public void BatchSizeOverrideWinsTest()
    {
        var chunk = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Cycles, 100, 12);
        Assert.That(chunk, Is.EqualTo(12));
    }

    [Test]
    public void BatchSizeOverrideClampedToFrameCountTest()
    {
        var chunk = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Eevee, 100, 500);
        Assert.That(chunk, Is.EqualTo(100));
    }

    [Test]
    public void ZeroOverrideFallsBackToHeuristicTest()
    {
        var withZero = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Cycles, 240, 0);
        var heuristic = RenderChunkPolicy.ComputeChunkSize(RenderEngine.Cycles, 240, 0);
        Assert.That(withZero, Is.EqualTo(heuristic));
        Assert.That(withZero, Is.EqualTo(5));
    }

    #endregion

    #region Degenerate Tests

    [Test]
    public void ZeroFrameCountReturnsOneTest()
    {
        Assert.That(RenderChunkPolicy.ComputeChunkSize(RenderEngine.Cycles, 0, 0), Is.EqualTo(1));
    }

    [Test]
    public void NegativeFrameCountReturnsOneTest()
    {
        Assert.That(RenderChunkPolicy.ComputeChunkSize(RenderEngine.Eevee, -5, 0), Is.EqualTo(1));
    }

    [Test]
    public void SingleFrameReturnsChunkOfOneTest()
    {
        Assert.That(RenderChunkPolicy.ComputeChunkSize(RenderEngine.Cycles, 1, 0), Is.EqualTo(1));
        Assert.That(RenderChunkPolicy.ComputeChunkSize(RenderEngine.Eevee, 1, 0), Is.EqualTo(1));
    }

    #endregion
}
