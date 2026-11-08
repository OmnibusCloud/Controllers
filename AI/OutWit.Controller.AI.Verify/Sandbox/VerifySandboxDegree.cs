namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// How many sandboxes a batch runs concurrently on a node: the cores, the memory (each task may
/// touch its memory cap, so the aggregate stays under the usable RAM) and a cap — 16 by default,
/// because a worker is often a desktop and a 32-core box should not vanish under a batch;
/// <c>VerifyOptions.MaxDegree</c> raises or lowers it per job.
/// </summary>
public static class VerifySandboxDegree
{
    #region Constants

    /// <summary>Most concurrent sandboxes a batch runs at when the options set no cap.</summary>
    public const int DEFAULT_MAX_DEGREE = 16;

    /// <summary>Share of the available RAM the sandboxes may claim together.</summary>
    public const double DEFAULT_RAM_HEADROOM = 0.75;

    #endregion

    #region Functions

    /// <summary>
    /// The degree for a machine: <c>min(cores, usableRam / perTaskCap, maxDegree)</c>, at least 1.
    /// </summary>
    /// <param name="logicalCores">Logical processors.</param>
    /// <param name="availableRamBytes">RAM the sandboxes may use in aggregate; 0 = unknown (cores decide).</param>
    /// <param name="perTaskMemoryCapBytes">The largest memory cap any task of the batch may reach; 0 = unknown.</param>
    /// <param name="maxDegree">The job's cap; 0 or less = <see cref="DEFAULT_MAX_DEGREE"/>.</param>
    /// <returns>The degree, at least 1.</returns>
    public static int Compute(int logicalCores, long availableRamBytes, long perTaskMemoryCapBytes, int maxDegree = DEFAULT_MAX_DEGREE)
    {
        var cap = maxDegree > 0 ? maxDegree : DEFAULT_MAX_DEGREE;
        var degree = Math.Min(Math.Max(1, logicalCores), cap);

        if (perTaskMemoryCapBytes <= 0 || availableRamBytes <= 0)
            return degree;

        var byMemory = (int)Math.Max(1, Math.Min(int.MaxValue, availableRamBytes / perTaskMemoryCapBytes));
        return Math.Max(1, Math.Min(degree, byMemory));
    }

    /// <summary>
    /// <see cref="Compute"/> for the current machine, with a fraction of the physical RAM as usable.
    /// </summary>
    /// <param name="perTaskMemoryCapBytes">The largest memory cap any task of the batch may reach.</param>
    /// <param name="maxDegree">The job's cap; 0 = the default.</param>
    /// <param name="ramHeadroomFraction">Share of the physical RAM the sandboxes may use.</param>
    /// <returns>The degree, at least 1.</returns>
    public static int ForCurrentMachine(long perTaskMemoryCapBytes, int maxDegree = DEFAULT_MAX_DEGREE, double ramHeadroomFraction = DEFAULT_RAM_HEADROOM)
    {
        // GC memory info exposes total physical memory portably; fall back to cores-only if unavailable.
        var info = GC.GetGCMemoryInfo();
        var totalRam = info.TotalAvailableMemoryBytes;
        var usableRam = (long)(totalRam * ramHeadroomFraction);
        return Compute(Environment.ProcessorCount, usableRam, perTaskMemoryCapBytes, maxDegree);
    }

    #endregion
}
