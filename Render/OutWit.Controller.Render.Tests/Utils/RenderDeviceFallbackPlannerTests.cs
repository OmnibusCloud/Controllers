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

    #region ResolveAfterFailure (crash-robust)

    [Test]
    public void ResolveAfterFailure_ForcedCpuFailure_IsTerminal()
    {
        var outcome = RenderDeviceFallbackPlanner.ResolveAfterFailure(
            requestedDevice: RenderDevice.CPU,
            reportedBackend: RenderDevice.CPU,
            availableGpuBackends: [],
            outOfMemory: false,
            alreadyFailedBackends: []);

        Assert.That(outcome.Done, Is.True);
        Assert.That(outcome.NextDevice, Is.Null);
    }

    [Test]
    public void ResolveAfterFailure_GpuCrashWithLostMarker_StillFallsBackToCpu()
    {
        // The crux of the Mac/Metal exit-134 case: the hard abort ate the backend marker, so Blender
        // reported NOTHING — but we auto-probed a GPU, so we must still retry on CPU (not give up).
        var outcome = RenderDeviceFallbackPlanner.ResolveAfterFailure(
            requestedDevice: null,            // auto-probe
            reportedBackend: null,            // marker lost to the crash
            availableGpuBackends: [],         // also lost
            outOfMemory: false,
            alreadyFailedBackends: []);

        Assert.That(outcome.Done, Is.False);
        Assert.That(outcome.NextDevice, Is.EqualTo(RenderDevice.CPU));
        Assert.That(outcome.BackendToBlacklist, Is.Null);   // unknown backend → don't blacklist
    }

    [Test]
    public void ResolveAfterFailure_ForcedGpuCrashWithLostMarker_FallsBackToCpu_WhenNoOtherGpu()
    {
        // Forced METAL (Mac) crashed; marker lost. We know it was METAL (we forced it), no other GPU → CPU.
        var outcome = RenderDeviceFallbackPlanner.ResolveAfterFailure(
            requestedDevice: RenderDevice.METAL,
            reportedBackend: null,
            availableGpuBackends: [RenderDevice.METAL],
            outOfMemory: false,
            alreadyFailedBackends: []);

        Assert.That(outcome.NextDevice, Is.EqualTo(RenderDevice.CPU));
        Assert.That(outcome.BackendToBlacklist, Is.EqualTo(RenderDevice.METAL));
    }

    [Test]
    public void ResolveAfterFailure_ReportedGpuCrash_ClimbsToNextGpuBackend()
    {
        var outcome = RenderDeviceFallbackPlanner.ResolveAfterFailure(
            requestedDevice: null,
            reportedBackend: RenderDevice.OPTIX,
            availableGpuBackends: [RenderDevice.OPTIX, RenderDevice.CUDA],
            outOfMemory: false,
            alreadyFailedBackends: []);

        Assert.That(outcome.NextDevice, Is.EqualTo(RenderDevice.CUDA));
        Assert.That(outcome.BackendToBlacklist, Is.EqualTo(RenderDevice.OPTIX));
    }

    [Test]
    public void ResolveAfterFailure_OutOfMemory_GoesToCpu_WithoutBlacklisting()
    {
        var outcome = RenderDeviceFallbackPlanner.ResolveAfterFailure(
            requestedDevice: null,
            reportedBackend: RenderDevice.OPTIX,
            availableGpuBackends: [RenderDevice.OPTIX, RenderDevice.CUDA],
            outOfMemory: true,
            alreadyFailedBackends: []);

        Assert.That(outcome.NextDevice, Is.EqualTo(RenderDevice.CPU));
        Assert.That(outcome.BackendToBlacklist, Is.Null);   // OOM is scene-specific, not a backend defect
    }

    #endregion
}
