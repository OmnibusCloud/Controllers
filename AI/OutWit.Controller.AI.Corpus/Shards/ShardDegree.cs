namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// How many shards a node processes at once: one per core (a shard is one streaming pass on
/// one thread), bounded by the memory the node gives to shards at a conservative per-shard
/// budget. The client starts every task of a parallel batch together; this is the ceiling that
/// keeps a small node from running twenty shards on four cores and two gigabytes.
/// <para>
/// A node's memory is budgeted once, in shares of what the process may use: half for the shards
/// in flight (<see cref="SHARD_SHARE"/>, at <see cref="MEMORY_PER_SHARD"/> each), a quarter for
/// the PDFs that run in the sandbox at the same time (<see cref="PDF_SHARE"/>, at the sandbox's
/// memory ceiling each), and a quarter left to the machine. The two shares used to be counted
/// each against the whole (all of it for shards and half of it again for PDFs).
/// </para>
/// </summary>
public static class ShardDegree
{
    #region Constants

    /// <summary>Memory budgeted per shard in flight: the record, the extractor's DOM, the zstd window, the buffers.</summary>
    public const long MEMORY_PER_SHARD = 256L * 1024 * 1024;

    /// <summary>The share of the memory the process may use that the shards in flight are budgeted from: 1 / this.</summary>
    public const int SHARD_SHARE = 2;

    /// <summary>The share of the memory the process may use that the PDFs running at once are budgeted from: 1 / this.</summary>
    public const int PDF_SHARE = 4;

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

        var byMemory = (int)Math.Min(int.MaxValue, availableBytes / SHARD_SHARE / MEMORY_PER_SHARD);
        return Math.Max(1, Math.Min(byCores, byMemory));
    }

    /// <summary>How many PDFs run in the sandbox at once on a machine: their memory ceilings within the PDFs' share of its memory, one at least, never more than its cores.</summary>
    /// <param name="memoryBytes">The memory ceiling of one run.</param>
    /// <param name="availableBytes">Memory the process may use (0 or less = unknown, cores decide).</param>
    /// <param name="cores">Logical processors.</param>
    /// <returns>At least 1.</returns>
    public static int PdfSlots(long memoryBytes, long availableBytes, int cores)
    {
        var byCores = Math.Max(1, cores);
        if (availableBytes <= 0)
            return byCores;

        var byMemory = (int)Math.Min(int.MaxValue, availableBytes / PDF_SHARE / Math.Max(1, memoryBytes));
        return Math.Clamp(byMemory, 1, byCores);
    }

    #endregion
}
