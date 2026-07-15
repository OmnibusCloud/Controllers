namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// How many tasks a node runs at once. A single WASM task is single-threaded, so
/// sequential execution would idle every core but one; but each concurrent store
/// costs its per-task memory cap, so the degree is bounded by BOTH cores and RAM —
/// not cores alone.
/// </summary>
public static class VerifySandboxDegree
{
    /// <summary>
    /// degree = clamp(1, min(logicalCores, availableRam / perTaskMemoryCap)).
    /// </summary>
    public static int Compute(int logicalCores, long availableRamBytes, long perTaskMemoryCapBytes)
    {
        var byCores = Math.Max(1, logicalCores);
        if (perTaskMemoryCapBytes <= 0 || availableRamBytes <= 0)
            return byCores;

        var byMemory = (int)Math.Max(1, availableRamBytes / perTaskMemoryCapBytes);
        return Math.Max(1, Math.Min(byCores, byMemory));
    }

    /// <summary>Degree for the current machine, leaving a RAM headroom fraction for the host.</summary>
    public static int ForCurrentMachine(long perTaskMemoryCapBytes, double ramHeadroomFraction = 0.75)
    {
        // GC memory info exposes total physical memory portably; fall back to cores-only if unavailable.
        var info = GC.GetGCMemoryInfo();
        var totalRam = info.TotalAvailableMemoryBytes;
        var usableRam = (long)(totalRam * ramHeadroomFraction);
        return Compute(Environment.ProcessorCount, usableRam, perTaskMemoryCapBytes);
    }
}
