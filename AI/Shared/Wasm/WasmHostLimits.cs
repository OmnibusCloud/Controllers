using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The envelope every store of a host gets: fuel, the cap of its one linear memory, caps on the
/// host memory a guest can claim outside that cap (tables, instances), and a wall-clock deadline
/// in epoch ticks. A memory grow past the cap is refused (memory.grow answers -1 to the guest):
/// the wasmtime 48 C API has no way to make a refused grow a trap, so a guest that cannot live
/// without the memory ends by its own hand (an allocator's abort, an interpreter's message), and
/// the host recognises that by the guest's words (<see cref="WasmHostRunRequest.OutOfMemory"/>).
/// </summary>
internal static class WasmHostLimits
{
    #region Constants

    /// <summary>
    /// The most elements one table of a guest may hold. A table is host memory the linear
    /// memory cap does not count (eight bytes an element or more), so it has a cap of its
    /// own: over a hundred times the largest table of Verify's bundled runtimes.
    /// </summary>
    public const uint MAX_TABLE_ELEMENTS = 1_048_576;

    /// <summary>Tables one guest may declare (a runtime module declares one).</summary>
    public const long MAX_TABLES = 8;

    /// <summary>One linear memory per guest: the memory cap is per memory, a second one would double it.</summary>
    public const long MAX_MEMORIES = 1;

    /// <summary>Instances one store may hold: the guest's own, with room for none of its making.</summary>
    public const long MAX_INSTANCES = 4;

    /// <summary>Epoch tick granularity of a host: the wall-clock interrupt resolution.</summary>
    public const int EPOCH_TICK_MS = 10;

    #endregion

    #region Functions

    /// <summary>
    /// Puts a store in its envelope: the fuel it may burn, the cap of its linear memory and of
    /// its tables and instances, and the epoch deadline of its wall time.
    /// </summary>
    /// <param name="store">A store not yet used.</param>
    /// <param name="fuel">The fuel the run may use (at least one unit is given).</param>
    /// <param name="memoryBytes">The cap of the guest's linear memory (at least one byte).</param>
    /// <param name="wallMs">The wall time the run may take; a backstop, fuel binds first.</param>
    public static void Apply(Store store, long fuel, long memoryBytes, int wallMs)
    {
        store.Fuel = (ulong)Math.Max(1, fuel);
        // The memory cap is per linear memory, so the guest gets one; tables are host
        // memory outside that cap, so their size and number are capped as well.
        store.SetLimits(
            memorySize: Math.Max(1, memoryBytes),
            tableElements: MAX_TABLE_ELEMENTS,
            instances: MAX_INSTANCES,
            tables: MAX_TABLES,
            memories: MAX_MEMORIES);
        store.SetEpochDeadline(WallToTicks(wallMs));
    }

    /// <summary>The epoch deadline of a wall-time budget: whole ticks, rounded up, at least one.</summary>
    /// <param name="wallMs">The wall time, milliseconds.</param>
    /// <returns>The deadline in ticks of <see cref="EPOCH_TICK_MS"/>.</returns>
    public static ulong WallToTicks(int wallMs)
    {
        var ticks = (wallMs + EPOCH_TICK_MS - 1) / EPOCH_TICK_MS;
        return (ulong)Math.Max(1, ticks);
    }

    #endregion
}
