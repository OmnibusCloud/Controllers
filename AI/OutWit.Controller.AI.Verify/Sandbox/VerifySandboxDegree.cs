namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// How many sandboxes a batch runs concurrently on a node: the cores, the memory (each task may
/// touch its memory cap, so the aggregate stays under the memory that is free) and a cap — 16 by
/// default, because a worker is often a desktop and a 32-core box should not vanish under a batch;
/// <c>VerifyOptions.MaxDegree</c> sets another cap for a job (the cores and the memory still
/// hold). The memory counted is what the machine has
/// free when the batch starts, not what it has in all: the modules the process already keeps
/// compiled, and everything else that runs on the machine, are in what is taken. A task of the
/// C/C++ runtime counts for its compiler's memory cap when that is the larger one.
/// </summary>
public static class VerifySandboxDegree
{
    #region Constants

    /// <summary>Most concurrent sandboxes a batch runs at when the options set no cap.</summary>
    public const int DEFAULT_MAX_DEGREE = 16;

    /// <summary>Share of the free RAM the sandboxes may claim together.</summary>
    public const double DEFAULT_RAM_HEADROOM = 0.75;

    #endregion

    #region Functions

    /// <summary>
    /// The degree for a machine: <c>min(cores, usableRam / perTaskCap, maxDegree)</c>, at least 1.
    /// </summary>
    /// <param name="logicalCores">Logical processors.</param>
    /// <param name="availableRamBytes">RAM the sandboxes may use in aggregate; 0 = unknown (cores decide).</param>
    /// <param name="perTaskMemoryCapBytes">The most memory any task of the batch may take; 0 = unknown.</param>
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
    /// <see cref="Compute"/> for the current machine, with a fraction of the RAM that is free as usable.
    /// </summary>
    /// <param name="perTaskMemoryCapBytes">The most memory any task of the batch may take.</param>
    /// <param name="maxDegree">The job's cap; 0 = the default.</param>
    /// <param name="ramHeadroomFraction">Share of the free RAM the sandboxes may use.</param>
    /// <returns>The degree, at least 1.</returns>
    public static int ForCurrentMachine(long perTaskMemoryCapBytes, int maxDegree = DEFAULT_MAX_DEGREE, double ramHeadroomFraction = DEFAULT_RAM_HEADROOM)
    {
        var freeRam = FreeRamBytes();
        var usableRam = freeRam <= 0 ? 0 : Math.Max(1, (long)(freeRam * ramHeadroomFraction));
        return Compute(Environment.ProcessorCount, usableRam, perTaskMemoryCapBytes, maxDegree);
    }

    /// <summary>
    /// The RAM that is free on this machine as the runtime last saw it: the total less the load
    /// of every process. The whole of it when the load is not known; 0 when nothing is.
    /// </summary>
    /// <returns>Bytes.</returns>
    public static long FreeRamBytes()
    {
        // The runtime's own view is portable across the three platforms; it is as fresh as the
        // last collection, which a process that runs batches has often.
        var info = GC.GetGCMemoryInfo();
        return FreeRamBytes(info.TotalAvailableMemoryBytes, info.MemoryLoadBytes);
    }

    /// <summary>The free RAM for a total and a load.</summary>
    /// <param name="totalBytes">The RAM the process may use in all; 0 or less = unknown.</param>
    /// <param name="loadBytes">The RAM in use on the machine; 0 or less = unknown.</param>
    /// <returns>Bytes: 0 when the total is unknown, at least 1 otherwise (a machine with nothing free runs one task at a time).</returns>
    public static long FreeRamBytes(long totalBytes, long loadBytes)
    {
        if (totalBytes <= 0)
            return 0;

        return loadBytes <= 0 ? totalBytes : Math.Max(1, totalBytes - loadBytes);
    }

    #endregion
}
