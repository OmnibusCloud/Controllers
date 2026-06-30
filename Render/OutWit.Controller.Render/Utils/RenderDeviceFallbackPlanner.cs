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
}
