using Wasmtime;
using WasmEngine = Wasmtime.Engine;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// WASI's own calls for one run, reached through the shim (<see cref="WasmHostShim"/>): the shim
/// is instantiated in the run's store on the first call that needs it, and every call through it
/// runs on fuel lent for the call and taken back after it, so the way through costs the guest
/// nothing (what the call costs is the meter's matter, <see cref="WasmHostMeter"/>).
/// <para>
/// The epoch deadline can stop a run inside the shim. There it is a trap with its code; once the
/// engine has carried it out through the host's call it is an error of that call whose text only
/// names the cause. So it is noted here, where the code is known.
/// </para>
/// </summary>
internal sealed class WasmHostWasi : IDisposable
{
    #region Constants

    // The fuel the shim's own instructions may take in one call; given before and taken back
    // after, so that a call through the shim costs the guest nothing.
    private const ulong SHIM_FUEL = 4096;

    #endregion

    #region Fields

    private readonly WasmEngine m_engine;
    private readonly WasmHostShim m_shimModule;
    private readonly WasmHostMeter m_meter;

    private Linker? m_wasi;
    private Instance? m_shim;

    #endregion

    #region Constructors

    /// <summary>
    /// WASI's own calls of one run.
    /// </summary>
    /// <param name="engine">The host's engine.</param>
    /// <param name="shim">The engine's shim.</param>
    /// <param name="meter">The run's meter.</param>
    public WasmHostWasi(WasmEngine engine, WasmHostShim shim, WasmHostMeter meter)
    {
        m_engine = engine;
        m_shimModule = shim;
        m_meter = meter;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Calls one of WASI's own definitions (a typed one of this class) through the shim.
    /// </summary>
    /// <param name="caller">The guest's call.</param>
    /// <param name="call">The call of the typed function, made once the shim stands.</param>
    /// <returns>What WASI answered, or "bad address" for a guest without a memory.</returns>
    public int Forward(Caller caller, Func<int> call)
    {
        var fuel = caller.Fuel;
        caller.Fuel = Lent(fuel);
        try
        {
            return Shim(caller) ? call() : WasmHostErrno.FAULT;
        }
        catch (TrapException trap) when (trap.Type == TrapCode.Interrupt)
        {
            m_meter.Interrupted();
            throw;
        }
        finally
        {
            caller.Fuel = fuel;
        }
    }

    /// <summary>
    /// Calls one of WASI's own definitions by its name, with the arguments as the guest gave them.
    /// </summary>
    /// <param name="caller">The guest's call.</param>
    /// <param name="name">The function's name.</param>
    /// <param name="arguments">The guest's arguments.</param>
    /// <returns>What WASI answered, or "bad address" for a guest without a memory.</returns>
    public int Pass(Caller caller, string name, ReadOnlySpan<ValueBox> arguments)
    {
        var fuel = caller.Fuel;
        caller.Fuel = Lent(fuel);
        try
        {
            if (!Shim(caller))
                return WasmHostErrno.FAULT;

            return m_shim!.GetFunction(name)?.Invoke(arguments) is int answer ? answer : WasmHostErrno.NOSYS;
        }
        catch (TrapException trap) when (trap.Type == TrapCode.Interrupt)
        {
            m_meter.Interrupted();
            throw;
        }
        finally
        {
            caller.Fuel = fuel;
        }
    }

    private static ulong Lent(ulong fuel)
    {
        return fuel > ulong.MaxValue - SHIM_FUEL ? ulong.MaxValue : fuel + SHIM_FUEL;
    }

    private bool Shim(Caller caller)
    {
        if (m_shim != null)
            return true;

        // WASI reads and writes the memory of the module that calls it: a guest without one has
        // nothing a file call could touch.
        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (memory == null)
            return false;

        m_wasi = new Linker(m_engine);
        m_wasi.DefineWasi();

        var shim = m_shimModule.Instantiate(m_wasi, caller.Store, memory);
        FdWrite = shim.GetFunction<int, int, int, int, int>(WasmHostShim.FD_WRITE);
        FdPwrite = shim.GetFunction<int, int, int, long, int, int>(WasmHostShim.FD_PWRITE);
        FdRead = shim.GetFunction<int, int, int, int, int>(WasmHostShim.FD_READ);
        FdPread = shim.GetFunction<int, int, int, long, int, int>(WasmHostShim.FD_PREAD);
        FdClose = shim.GetFunction<int, int>(WasmHostShim.FD_CLOSE);
        FdSeek = shim.GetFunction<int, long, int, int, int>(WasmHostShim.FD_SEEK);
        FdFilestatGet = shim.GetFunction<int, int, int>(WasmHostShim.FD_FILESTAT_GET);
        FdFilestatSetSize = shim.GetFunction<int, long, int>(WasmHostShim.FD_FILESTAT_SET_SIZE);
        FdAllocate = shim.GetFunction<int, long, long, int>(WasmHostShim.FD_ALLOCATE);
        PathOpen = shim.GetFunction<int, int, int, int, int, long, long, int, int, int>(WasmHostShim.PATH_OPEN);
        PathFilestatGet = shim.GetFunction<int, int, int, int, int, int>(WasmHostShim.PATH_FILESTAT_GET);
        m_shim = shim;
        return true;
    }

    #endregion

    #region IDisposable

    /// <summary>Releases the linker the shim was made with.</summary>
    public void Dispose()
    {
        m_wasi?.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>WASI's <c>fd_write</c>; set once the shim stands.</summary>
    public Func<int, int, int, int, int>? FdWrite { get; private set; }

    /// <summary>WASI's <c>fd_pwrite</c>; set once the shim stands.</summary>
    public Func<int, int, int, long, int, int>? FdPwrite { get; private set; }

    /// <summary>WASI's <c>fd_read</c>; set once the shim stands.</summary>
    public Func<int, int, int, int, int>? FdRead { get; private set; }

    /// <summary>WASI's <c>fd_pread</c>; set once the shim stands.</summary>
    public Func<int, int, int, long, int, int>? FdPread { get; private set; }

    /// <summary>WASI's <c>fd_close</c>; set once the shim stands.</summary>
    public Func<int, int>? FdClose { get; private set; }

    /// <summary>WASI's <c>fd_seek</c>; set once the shim stands.</summary>
    public Func<int, long, int, int, int>? FdSeek { get; private set; }

    /// <summary>WASI's <c>fd_filestat_get</c>; set once the shim stands.</summary>
    public Func<int, int, int>? FdFilestatGet { get; private set; }

    /// <summary>WASI's <c>fd_filestat_set_size</c>; set once the shim stands.</summary>
    public Func<int, long, int>? FdFilestatSetSize { get; private set; }

    /// <summary>WASI's <c>fd_allocate</c>; set once the shim stands.</summary>
    public Func<int, long, long, int>? FdAllocate { get; private set; }

    /// <summary>WASI's <c>path_open</c>; set once the shim stands.</summary>
    public Func<int, int, int, int, int, long, long, int, int, int>? PathOpen { get; private set; }

    /// <summary>WASI's <c>path_filestat_get</c>; set once the shim stands.</summary>
    public Func<int, int, int, int, int, int>? PathFilestatGet { get; private set; }

    #endregion
}
