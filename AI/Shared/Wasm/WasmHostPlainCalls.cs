using System.Text;
using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The calls of WASI that the host has no reason to look into, for a run whose calls cost fuel:
/// each is defined by the host all the same, so that it is paid for (the fixed price of a call,
/// and the bytes it moves where the call says how many), and then handed to WASI's own definition
/// as it came. Without that a program could spend the host's time in them for a few units a call:
/// the arguments of a run copied into the guest again and again, a sleep that never ends.
/// The names the host answers itself (<see cref="WasmHostSyscalls"/>, <see cref="WasmHostLinker"/>,
/// <see cref="WasmHostExit"/>) are defined after these and stand over them.
/// </summary>
internal static class WasmHostPlainCalls
{
    #region Constants

    private const string WASI_MODULE = "wasi_snapshot_preview1";

    private const string ARGS_GET = "args_get";
    private const string ENVIRON_GET = "environ_get";

    private const ValueKind I32 = ValueKind.Int32;
    private const ValueKind I64 = ValueKind.Int64;

    /// <summary>The bytes of a pointer in the guest: what the list of a run's arguments holds one of for each.</summary>
    private const int POINTER_BYTES = 4;

    #endregion

    #region Fields

    private static readonly ValueKind[] ERRNO = [I32];

    /// <summary>
    /// The calls: the name, the parameters, and the parameters that are a count of bytes the call
    /// reads or writes in the guest's memory (the pointer is the parameter before each).
    /// </summary>
    internal static readonly (string Name, ValueKind[] Parameters, int[] Lengths)[] CALLS =
    [
        (ARGS_GET, [I32, I32], []),
        ("args_sizes_get", [I32, I32], []),
        (ENVIRON_GET, [I32, I32], []),
        ("environ_sizes_get", [I32, I32], []),
        ("fd_advise", [I32, I64, I64, I32], []),
        (WasmHostShim.FD_ALLOCATE, [I32, I64, I64], []),
        ("fd_datasync", [I32], []),
        ("fd_fdstat_get", [I32, I32], []),
        ("fd_fdstat_set_flags", [I32, I32], []),
        ("fd_fdstat_set_rights", [I32, I64, I64], []),
        (WasmHostShim.FD_FILESTAT_SET_SIZE, [I32, I64], []),
        ("fd_prestat_get", [I32, I32], []),
        ("fd_prestat_dir_name", [I32, I32, I32], [2]),
        (WasmHostShim.FD_SEEK, [I32, I64, I32, I32], []),
        ("fd_sync", [I32], []),
        ("fd_tell", [I32, I32], []),
        ("path_create_directory", [I32, I32, I32], [2]),
        ("path_link", [I32, I32, I32, I32, I32, I32, I32], [3, 6]),
        ("path_readlink", [I32, I32, I32, I32, I32, I32], [2, 4]),
        ("path_remove_directory", [I32, I32, I32], [2]),
        ("path_rename", [I32, I32, I32, I32, I32, I32], [2, 5]),
        ("path_symlink", [I32, I32, I32, I32, I32], [1, 4]),
        ("path_unlink_file", [I32, I32, I32], [2]),
        ("proc_raise", [I32], []),
        ("sched_yield", [], []),
        ("sock_accept", [I32, I32, I32], []),
        ("sock_recv", [I32, I32, I32, I32, I32, I32], []),
        ("sock_send", [I32, I32, I32, I32, I32], []),
        ("sock_shutdown", [I32, I32], [])
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Defines the calls in a linker that has WASI and allows shadowing.
    /// </summary>
    /// <param name="linker">The run's linker.</param>
    /// <param name="meter">The run's meter.</param>
    /// <param name="wasi">The run's way to WASI's own calls.</param>
    /// <param name="request">The run.</param>
    public static void Define(Linker linker, WasmHostMeter meter, WasmHostWasi wasi, WasmHostRunRequest request)
    {
        var argumentBytes = ListBytes(request.Argv.Select(argument => Encoding.UTF8.GetByteCount(argument)));
        var environmentBytes = ListBytes(request.Environment.Select(pair => Encoding.UTF8.GetByteCount(pair.Name) + 1 + Encoding.UTF8.GetByteCount(pair.Value)));

        foreach (var (name, parameters, lengths) in CALLS)
        {
            // What the call copies into the guest whatever its arguments say.
            var copied = name switch
            {
                ARGS_GET => argumentBytes,
                ENVIRON_GET => environmentBytes,
                _ => 0
            };

            linker.DefineFunction(WASI_MODULE, name, (Caller caller, ReadOnlySpan<ValueBox> arguments, Span<ValueBox> results) =>
            {
                meter.Call(caller, name);
                meter.Bytes(caller, name, copied + Moved(caller, arguments, lengths));
                results[0] = wasi.Pass(caller, name, arguments);
            }, parameters, ERRNO);
        }
    }

    /// <summary>The bytes a list of strings takes in the guest: each with its end and its pointer.</summary>
    private static long ListBytes(IEnumerable<int> lengths)
    {
        long bytes = 0;
        foreach (var length in lengths)
            bytes += length + 1 + POINTER_BYTES;

        return bytes;
    }

    /// <summary>
    /// The bytes a call says it moves. A count that reaches past the guest's memory moves nothing:
    /// WASI answers such a call with "bad address".
    /// </summary>
    private static long Moved(Caller caller, ReadOnlySpan<ValueBox> arguments, int[] lengths)
    {
        if (lengths.Length == 0)
            return 0;

        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (memory == null)
            return 0;

        long bytes = 0;
        foreach (var index in lengths)
        {
            long count = (uint)arguments[index].AsInt32();
            if (WasmHostMemory.InRange(memory, arguments[index - 1].AsInt32(), count))
                bytes += count;
        }

        return bytes;
    }

    #endregion
}
