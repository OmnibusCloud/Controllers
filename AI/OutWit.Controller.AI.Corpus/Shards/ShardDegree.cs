namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// How many shards a node processes at once: one per core (a shard is one streaming pass on
/// one thread), bounded by the memory the process may use at a conservative per-shard budget.
/// The client starts every task of a parallel batch together; this is the ceiling that keeps a
/// small node from running twenty shards on four cores and two gigabytes.
/// </summary>
public static class ShardDegree
{
    #region Constants

    /// <summary>Memory budgeted per shard in flight: the record, the extractor's DOM, the zstd window, the buffers.</summary>
    public const long MEMORY_PER_SHARD = 256L * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>The degree of this machine.</summary>
    /// <returns>At least 1.</returns>
    public static int ForCurrentMachine()
    {
        return For(Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
    }

    /// <summary>The degree for a core count and a memory size.</summary>
    /// <param name="cores">Logical processors.</param>
    /// <param name="availableBytes">Memory the process may use (0 or less = unknown, cores decide).</param>
    /// <returns>At least 1.</returns>
    public static int For(int cores, long availableBytes)
    {
        var byCores = Math.Max(1, cores);
        if (availableBytes <= 0)
            return byCores;

        var byMemory = (int)Math.Min(int.MaxValue, availableBytes / MEMORY_PER_SHARD);
        return Math.Max(1, Math.Min(byCores, byMemory));
    }

    #endregion
}
