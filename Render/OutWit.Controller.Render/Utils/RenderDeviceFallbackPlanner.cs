namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Pure decision logic for the smart render-device fallback. When a GPU render attempt fails, this
/// decides what to try next:
/// <list type="bullet">
/// <item>a GPU out-of-memory failure → go straight to CPU (every GPU backend shares the same VRAM,
/// so the next one would OOM too);</item>
/// <item>any other GPU crash (e.g. OptiX on an older card) → the next AVAILABLE GPU backend in
/// candidate order that has not already failed, and only when none remain → CPU.</item>
/// </list>
/// Stateless and side-effect free so the ladder is unit-testable; <see cref="BlenderRunner"/> owns the
/// process orchestration and the per-node memo of which backends are known good/bad.
/// </summary>
internal static class RenderDeviceFallbackPlanner
{
    /// <summary>GPU backends in the order Cycles should prefer them.</summary>
    private static readonly RenderDevice[] GPU_BACKEND_ORDER = [RenderDevice.OPTIX, RenderDevice.CUDA, RenderDevice.HIP, RenderDevice.METAL];

    /// <summary>
    /// The device to attempt after <paramref name="failedBackend"/> failed. Returns a GPU backend to
    /// try next, or <see cref="RenderDevice.CPU"/> when GPU options are exhausted (or on out-of-memory,
    /// which no GPU backend can rescue).
    /// </summary>
    public static RenderDevice NextDevice(
        RenderDevice failedBackend,
        IReadOnlyCollection<RenderDevice> availableGpuBackends,
        bool outOfMemory,
        IReadOnlyCollection<RenderDevice> alreadyFailedBackends)
    {
        if (outOfMemory)
            return RenderDevice.CPU;

        var exhausted = new HashSet<RenderDevice>(alreadyFailedBackends) { failedBackend };

        foreach (var backend in GPU_BACKEND_ORDER)
        {
            if (availableGpuBackends.Contains(backend) && !exhausted.Contains(backend))
                return backend;
        }

        return RenderDevice.CPU;
    }

    /// <summary>
    /// The GPU backend to PREFER for a node's first attempt, given what is known from earlier renders
    /// on this node: the last backend that succeeded (if still considered good), otherwise the first
    /// available backend not known to be bad. Returns <c>null</c> to let Blender auto-probe (no usable
    /// memo yet).
    /// </summary>
    public static RenderDevice? PreferredFirstAttempt(
        RenderDevice? knownGoodBackend,
        IReadOnlyCollection<RenderDevice> knownBadBackends)
    {
        if (knownGoodBackend is { } good && IsGpuBackend(good) && !knownBadBackends.Contains(good))
            return good;

        return null;
    }

    public static bool IsGpuBackend(RenderDevice device)
    {
        return device is RenderDevice.CUDA or RenderDevice.OPTIX or RenderDevice.HIP or RenderDevice.METAL;
    }

    /// <summary>What to do after a render attempt FAILED (non-zero exit).</summary>
    /// <param name="Done">No fallback left — surface the failure.</param>
    /// <param name="NextDevice">The device to retry on (non-null when not <see cref="Done"/>).</param>
    /// <param name="BackendToBlacklist">A GPU backend that genuinely crashed and should be remembered
    /// as bad for this node (null for OOM — VRAM pressure is scene-specific, not a backend defect — or
    /// when the failed backend is unknown).</param>
    public readonly record struct AttemptOutcome(bool Done, RenderDevice? NextDevice, RenderDevice? BackendToBlacklist);

    /// <summary>
    /// Decide the next step after a failed attempt. Keys on the device we REQUESTED (always known), not
    /// only the one Blender reported — a hard crash can abort before the backend marker is flushed, and
    /// we must still fall back. A forced-CPU failure is terminal; OOM or an unknown failed backend steps
    /// straight to CPU; a known GPU crash climbs to the next available GPU backend, then CPU.
    /// </summary>
    public static AttemptOutcome ResolveAfterFailure(
        RenderDevice? requestedDevice,
        RenderDevice? reportedBackend,
        IReadOnlyCollection<RenderDevice> availableGpuBackends,
        bool outOfMemory,
        IReadOnlyCollection<RenderDevice> alreadyFailedBackends)
    {
        if (requestedDevice == RenderDevice.CPU)
            return new AttemptOutcome(Done: true, NextDevice: null, BackendToBlacklist: null);

        // The backend that failed: what Blender reported, else the specific GPU we forced. Null only when
        // we auto-probed and the marker was lost to a crash.
        var failed = reportedBackend
            ?? (requestedDevice is { } requested && IsGpuBackend(requested) ? requested : (RenderDevice?)null);

        if (outOfMemory || failed is null)
            return new AttemptOutcome(Done: false, NextDevice: RenderDevice.CPU, BackendToBlacklist: null);

        var next = NextDevice(failed.Value, availableGpuBackends, outOfMemory: false, alreadyFailedBackends);
        return new AttemptOutcome(Done: false, NextDevice: next, BackendToBlacklist: failed.Value);
    }
}
