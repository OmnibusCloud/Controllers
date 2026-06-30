using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Unit tests for the smart render-device fallback decision logic (the ladder a node climbs down when a
/// GPU render fails): next available GPU backend on a crash, straight to CPU on out-of-memory, and the
/// per-node "preferred first attempt" memo.
/// </summary>
[TestFixture]
internal sealed class RenderDeviceFallbackPlannerTests
{
    private static readonly RenderDevice[] OptixAndCuda = [RenderDevice.OPTIX, RenderDevice.CUDA];

    #region NextDevice

    [Test]
    public void NextDevice_FallsToNextAvailableGpuBackend_OnNonOomCrash()
    {
        // OptiX crashed (e.g. on a Pascal card) but CUDA is available → try CUDA, NOT CPU.
        var next = RenderDeviceFallbackPlanner.NextDevice(
            failedBackend: RenderDevice.OPTIX,
            availableGpuBackends: OptixAndCuda,
            outOfMemory: false,
            alreadyFailedBackends: []);

        Assert.That(next, Is.EqualTo(RenderDevice.CUDA));
    }

    [Test]
    public void NextDevice_GoesStraightToCpu_OnOutOfMemory()
    {
        // Every GPU backend shares the same VRAM — another GPU backend would OOM too, so skip to CPU.
        var next = RenderDeviceFallbackPlanner.NextDevice(
            failedBackend: RenderDevice.OPTIX,
            availableGpuBackends: OptixAndCuda,
            outOfMemory: true,
            alreadyFailedBackends: []);

        Assert.That(next, Is.EqualTo(RenderDevice.CPU));
    }

    [Test]
    public void NextDevice_FallsToCpu_WhenAllGpuBackendsExhausted()
    {
        var next = RenderDeviceFallbackPlanner.NextDevice(
            failedBackend: RenderDevice.CUDA,
            availableGpuBackends: OptixAndCuda,
            outOfMemory: false,
            alreadyFailedBackends: [RenderDevice.OPTIX]);

        Assert.That(next, Is.EqualTo(RenderDevice.CPU));
    }

    [Test]
    public void NextDevice_FallsToCpu_WhenNoOtherGpuBackendAvailable()
    {
        // Only OptiX enumerated; it crashed → nothing else to try → CPU.
        var next = RenderDeviceFallbackPlanner.NextDevice(
            failedBackend: RenderDevice.OPTIX,
            availableGpuBackends: [RenderDevice.OPTIX],
            outOfMemory: false,
            alreadyFailedBackends: []);

        Assert.That(next, Is.EqualTo(RenderDevice.CPU));
    }

    [Test]
    public void NextDevice_RespectsCandidateOrder()
    {
        // CUDA failed, both HIP and CUDA available plus OPTIX (not yet tried) → OptiX first by order.
        var next = RenderDeviceFallbackPlanner.NextDevice(
            failedBackend: RenderDevice.CUDA,
            availableGpuBackends: [RenderDevice.OPTIX, RenderDevice.CUDA, RenderDevice.HIP],
            outOfMemory: false,
            alreadyFailedBackends: []);

        Assert.That(next, Is.EqualTo(RenderDevice.OPTIX));
    }

    #endregion

    #region PreferredFirstAttempt

    [Test]
    public void PreferredFirstAttempt_ReturnsNull_WhenNothingLearnedYet()
    {
        Assert.That(RenderDeviceFallbackPlanner.PreferredFirstAttempt(knownGoodBackend: null, knownBadBackends: []), Is.Null);
    }

    [Test]
    public void PreferredFirstAttempt_PrefersKnownGoodBackend()
    {
        // The node learned CUDA works → force it first instead of re-probing (and re-crashing) OptiX.
        var preferred = RenderDeviceFallbackPlanner.PreferredFirstAttempt(
            knownGoodBackend: RenderDevice.CUDA,
            knownBadBackends: [RenderDevice.OPTIX]);

        Assert.That(preferred, Is.EqualTo(RenderDevice.CUDA));
    }

    [Test]
    public void PreferredFirstAttempt_IgnoresKnownGood_WhenItIsAlsoMarkedBad()
    {
        // Defensive: if the known-good backend later turned bad, fall back to auto-probe (null).
        var preferred = RenderDeviceFallbackPlanner.PreferredFirstAttempt(
            knownGoodBackend: RenderDevice.CUDA,
            knownBadBackends: [RenderDevice.CUDA]);

        Assert.That(preferred, Is.Null);
    }

    [Test]
    public void PreferredFirstAttempt_IgnoresCpuAsKnownGood()
    {
        // CPU is never a "preferred GPU first attempt" — auto-probe so a recovered GPU is retried.
        var preferred = RenderDeviceFallbackPlanner.PreferredFirstAttempt(
            knownGoodBackend: RenderDevice.CPU,
            knownBadBackends: []);

        Assert.That(preferred, Is.Null);
    }

    #endregion
}
