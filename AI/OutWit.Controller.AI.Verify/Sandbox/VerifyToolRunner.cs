using OutWit.Controller.AI.Wasm;
using Wasmtime;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Runs a tool module the sandbox uses on a task's behalf (the compiler and the linker of the
/// C/C++ runtime) in a store of its own, under the same envelope as a program: fuel, an epoch
/// deadline, the memory and table caps (<see cref="WasmHostLimits"/>), the deterministic linker,
/// read-only folders only. Its stdout is its product (an object, a module), written straight to
/// a file; its stderr is the diagnostics a task's author reads.
/// </summary>
internal sealed class VerifyToolRunner
{
    #region Constants

    private const string START_EXPORT = "_start";

    #endregion

    #region Fields

    private readonly WasmHost m_host;

    #endregion

    #region Constructors

    /// <summary>
    /// A runner over the sandbox's host.
    /// </summary>
    /// <param name="host">The host whose engine every store shares (fuel and epoch on).</param>
    public VerifyToolRunner(WasmHost host)
    {
        m_host = host;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs the tool once.
    /// </summary>
    /// <param name="module">The tool's compiled module.</param>
    /// <param name="argv">argv, argv[0] included.</param>
    /// <param name="folders">Host folders preopened read-only, with the guest path of each.</param>
    /// <param name="stdoutPath">The file the tool's stdout goes to.</param>
    /// <param name="stderrPath">The file the tool's stderr goes to.</param>
    /// <param name="fuel">The fuel the run may use.</param>
    /// <param name="wallMs">The wall time the run may take.</param>
    /// <param name="memoryBytes">The cap of the tool's linear memory.</param>
    /// <returns>How the run ended and what it cost.</returns>
    public VerifyToolOutcome Run(WasmModule module, IReadOnlyList<string> argv, IReadOnlyList<(string Host, string Guest)> folders,
        string stdoutPath, string stderrPath, long fuel, int wallMs, long memoryBytes)
    {
        var wasi = new WasiConfiguration().WithArgs(argv).WithStandardOutput(stdoutPath).WithStandardError(stderrPath);
        foreach (var (host, guest) in folders)
            wasi = wasi.WithPreopenedDirectory(host, guest, false);

        var started = DateTime.UtcNow;
        using var store = new Store(m_host.Engine);
        store.SetWasiConfiguration(wasi);
        WasmHostLimits.Apply(store, fuel, memoryBytes, wallMs);

        // A tool takes no randomness; the seed only keeps the linker's stream fixed.
        using var linker = WasmHostLinker.Create(m_host.Engine, 0);

        var (exitCode, trap) = WasmHostGuestThread.Run(() => Invoke(linker, store, module));

        var used = trap is TrapCode.OutOfFuel ? fuel : fuel - (long)store.Fuel;
        return new VerifyToolOutcome(exitCode, trap, used, (int)(DateTime.UtcNow - started).TotalMilliseconds);
    }

    private static (int ExitCode, TrapCode? Trap) Invoke(Linker linker, Store store, WasmModule module)
    {
        try
        {
            var instance = linker.Instantiate(store, module);
            var start = instance.GetAction(START_EXPORT) ?? throw new InvalidOperationException($"the tool exports no {START_EXPORT}");
            start();
            return (0, null);
        }
        catch (WasmtimeException exception) when (exception.ExitCode is { } code)
        {
            return (code, null);
        }
        catch (TrapException exception)
        {
            return (-1, exception.Type);
        }
    }

    #endregion
}
