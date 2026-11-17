using System.Buffers.Binary;
using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// What the host checks of a guest's memory before it uses a pointer the guest handed it. A
/// pointer is the guest's: an unsigned 32-bit number that may point anywhere. One outside the
/// memory is the guest's own error ("bad address"), never an exception of the engine.
/// </summary>
internal static class WasmHostMemory
{
    #region Constants

    /// <summary>The most buffers one read or write may name (the C library's own limit).</summary>
    public const int MAX_BUFFERS = 1024;

    private const int IOVEC_SIZE = 8;
    private const int IOVEC_LENGTH_OFFSET = 4;

    #endregion

    #region Functions

    /// <summary>
    /// Whether a range the guest named lies inside its memory.
    /// </summary>
    /// <param name="memory">The guest's memory.</param>
    /// <param name="pointer">Where the range starts, as the guest gave it.</param>
    /// <param name="length">Its length in bytes.</param>
    /// <returns>True when every byte of the range is the guest's.</returns>
    public static bool InRange(Memory memory, int pointer, long length)
    {
        long start = (uint)pointer;
        return length >= 0 && start + length <= memory.GetLength();
    }

    /// <summary>
    /// The first buffer of a read's or a write's list that holds anything: the one WASI's own
    /// call takes.
    /// </summary>
    /// <param name="memory">The guest's memory.</param>
    /// <param name="buffers">Where the list starts.</param>
    /// <param name="count">How many buffers it names.</param>
    /// <param name="pointer">Where the buffer starts; 0 when every buffer is empty.</param>
    /// <param name="length">Its length; 0 when every buffer is empty.</param>
    /// <returns>WASI's errno: success, "invalid" for a list longer than <see cref="MAX_BUFFERS"/>, "bad address" for a list or a buffer outside the memory.</returns>
    public static int FirstBuffer(Memory memory, int buffers, int count, out long pointer, out int length)
    {
        pointer = 0;
        length = 0;

        if ((uint)count > MAX_BUFFERS)
            return WasmHostErrno.INVAL;

        if (!InRange(memory, buffers, (long)count * IOVEC_SIZE))
            return WasmHostErrno.FAULT;

        var size = memory.GetLength();
        for (var index = 0; index < count; index++)
        {
            var record = memory.GetSpan((uint)buffers + (long)index * IOVEC_SIZE, IOVEC_SIZE);
            var bytes = BinaryPrimitives.ReadUInt32LittleEndian(record[IOVEC_LENGTH_OFFSET..]);
            if (bytes == 0)
                continue;

            var start = BinaryPrimitives.ReadUInt32LittleEndian(record);
            if (start + (long)bytes > size)
                return WasmHostErrno.FAULT;

            pointer = start;
            length = (int)Math.Min(bytes, int.MaxValue);
            return WasmHostErrno.SUCCESS;
        }

        return WasmHostErrno.SUCCESS;
    }

    #endregion
}
