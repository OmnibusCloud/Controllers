using System.Text;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The way from a host function to WASI's own. The host defines a guest's file calls itself
/// (<see cref="WasmHostSyscalls"/>) so that it can bound and count them, and hands the ones it lets
/// through to wasmtime's WASI. That WASI finds the guest's memory through the module that calls it,
/// so a host function cannot call it directly ("missing required memory export"): a small module
/// that imports the guest's memory, exports it under the name WASI looks for, and forwards each
/// call stands in between. It is compiled once an engine and instantiated in a run's store on the
/// first call that needs it. It forwards the calls the host looks into (<see cref="WasmHostSyscalls"/>)
/// and the ones it only meters (<see cref="WasmHostPlainCalls"/>).
/// </summary>
internal sealed class WasmHostShim : IDisposable
{
    #region Constants

    /// <summary>The import the shim takes the guest's memory by.</summary>
    public const string GUEST_MODULE = "guest";

    /// <summary>The name of a guest's memory, as WASI looks for it.</summary>
    public const string MEMORY = "memory";

    private const string WASI_MODULE = "wasi_snapshot_preview1";
    private const string SHIM_NAME = "wasm-host-shim";

    private const string I32 = "i32";
    private const string I64 = "i64";

    public const string FD_WRITE = "fd_write";
    public const string FD_PWRITE = "fd_pwrite";
    public const string FD_READ = "fd_read";
    public const string FD_PREAD = "fd_pread";
    public const string FD_CLOSE = "fd_close";
    public const string FD_SEEK = "fd_seek";
    public const string FD_FILESTAT_GET = "fd_filestat_get";
    public const string FD_FILESTAT_SET_SIZE = "fd_filestat_set_size";
    public const string FD_ALLOCATE = "fd_allocate";
    public const string PATH_OPEN = "path_open";
    public const string PATH_FILESTAT_GET = "path_filestat_get";

    private static readonly (string Name, string[] Parameters)[] FORWARDED =
    [
        (FD_WRITE, [I32, I32, I32, I32]),
        (FD_PWRITE, [I32, I32, I32, I64, I32]),
        (FD_READ, [I32, I32, I32, I32]),
        (FD_PREAD, [I32, I32, I32, I64, I32]),
        (FD_CLOSE, [I32]),
        (FD_SEEK, [I32, I64, I32, I32]),
        (FD_FILESTAT_GET, [I32, I32]),
        (FD_FILESTAT_SET_SIZE, [I32, I64]),
        (FD_ALLOCATE, [I32, I64, I64]),
        (PATH_OPEN, [I32, I32, I32, I32, I32, I64, I64, I32, I32]),
        (PATH_FILESTAT_GET, [I32, I32, I32, I32, I32])
    ];

    #endregion

    #region Fields

    private readonly WasmModule m_module;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Compiles the shim on an engine.
    /// </summary>
    /// <param name="engine">The host's engine.</param>
    public WasmHostShim(WasmEngine engine)
    {
        m_module = WasmModule.FromText(engine, SHIM_NAME, Text());
    }

    #endregion

    #region Functions

    /// <summary>
    /// Instantiates the shim in a run's store, over the guest's memory.
    /// </summary>
    /// <param name="wasi">A linker with WASI's own definitions and nothing of the host's over them.</param>
    /// <param name="store">The run's store.</param>
    /// <param name="memory">The guest's memory.</param>
    /// <returns>The instance, whose exports are WASI's own calls.</returns>
    public Instance Instantiate(Linker wasi, Store store, Memory memory)
    {
        wasi.Define(GUEST_MODULE, MEMORY, memory);
        return wasi.Instantiate(store, m_module);
    }

    /// <summary>
    /// The shim as text: the guest's memory imported and exported again, and one function a
    /// forwarded call that passes its arguments on.
    /// </summary>
    /// <returns>The module in the WebAssembly text format.</returns>
    public static string Text()
    {
        var text = new StringBuilder();
        var forwarded = Forwarded();

        text.Append("(module\n");
        text.Append($"  (import \"{GUEST_MODULE}\" \"{MEMORY}\" (memory 0))\n");

        foreach (var (name, parameters) in forwarded)
            text.Append($"  (import \"{WASI_MODULE}\" \"{name}\" (func ${name}{Parameters(parameters)} (result {I32})))\n");

        text.Append($"  (export \"{MEMORY}\" (memory 0))\n");

        foreach (var (name, parameters) in forwarded)
        {
            text.Append($"  (func (export \"{name}\"){Parameters(parameters)} (result {I32})");
            for (var index = 0; index < parameters.Length; index++)
                text.Append($" local.get {index}");
            text.Append($" call ${name})\n");
        }

        text.Append(")\n");
        return text.ToString();
    }

    /// <summary>Every call the shim forwards, once: the ones the host looks into, then the ones it only meters.</summary>
    private static List<(string Name, string[] Parameters)> Forwarded()
    {
        var forwarded = FORWARDED.ToList();
        foreach (var (name, parameters, _) in WasmHostPlainCalls.CALLS)
        {
            if (forwarded.All(call => call.Name != name))
                forwarded.Add((name, parameters.Select(kind => kind == ValueKind.Int64 ? I64 : I32).ToArray()));
        }

        return forwarded;
    }

    private static string Parameters(string[] parameters)
    {
        return parameters.Length == 0 ? "" : $" (param {string.Join(' ', parameters)})";
    }

    #endregion

    #region IDisposable

    /// <summary>Releases the compiled shim; no store may still hold an instance of it.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_module.Dispose();
    }

    #endregion
}
