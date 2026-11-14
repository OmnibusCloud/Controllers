using System.Runtime.CompilerServices;
using System.Text;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The wasm sandbox host a controller carries as its own compiled-in copy: one wasmtime engine
/// with the deterministic configuration (<see cref="WasmHostEngineConfig"/>), one timer that
/// advances its epoch, and the run of one WASI command module in a fresh store within a fuel /
/// memory / wall-clock envelope, mapped to a <see cref="WasmHostVerdict"/>. Modules are compiled
/// on <see cref="Engine"/> by the caller and may be run many times, concurrently, from any thread.
/// <para>
/// The host refuses to start, with a message naming both versions, when the loaded binding is not
/// the one the controller was built against (<see cref="WasmHostBinding"/>); nothing of it starts
/// then, and no run can fail for that reason later.
/// </para>
/// </summary>
internal sealed class WasmHost : IDisposable
{
    #region Constants

    private const string GUEST_MEMORY = "memory";
    private const string START_EXPORT = "_start";

    /// <summary>The fault of a run the engine ended without a trap or an exit code.</summary>
    public const string ENGINE_ERROR_FAULT = "wasm error: the engine refused a call of the guest";

    #endregion

    #region Fields

    private readonly WasmEngine m_engine;
    private readonly Timer m_epochTimer;
    private readonly bool m_explicitTraps;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a host: the binding guard first, then the module-aware native resolver (before the
    /// first wasmtime P/Invoke: in the shared load context the cloud client uses, the native library
    /// is found only through it), then one engine and one epoch timer.
    /// </summary>
    /// <exception cref="InvalidOperationException">The loaded binding is not the one built against, or it lacks what the engine settings need.</exception>
    public WasmHost()
    {
        WasmHostBinding.ThrowIfUnavailable();
        WasmHostNativeLibrary.EnsureRegistered();

        m_engine = CreateEngine(out m_explicitTraps);

        // One timer thread advances the epoch for every store; per-store deadlines
        // (SetEpochDeadline) convert a wall-time budget into a tick count.
        m_epochTimer = new Timer(AdvanceEpoch, null, WasmHostLimits.EPOCH_TICK_MS, WasmHostLimits.EPOCH_TICK_MS);
    }

    /// <summary>
    /// Makes the engine. Kept out of line, so that every member of the binding the engine needs is
    /// looked up after the guard has passed, not when the constructor is compiled.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WasmEngine CreateEngine(out bool explicitTraps)
    {
        return new WasmEngine(WasmHostEngineConfig.Create(out explicitTraps));
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs one WASI command module once, in its own store: the request's argv, environment,
    /// folders and stdin, the deterministic clock and randomness, the fuel / memory / wall-clock
    /// envelope; stdout and stderr captured in files under the request's capture folder and read
    /// back capped. Runs on a thread that holds the guest's stack (<see cref="WasmHostGuestThread"/>).
    /// Never throws for a guest-side fault: every way the guest can end is a verdict.
    /// </summary>
    /// <param name="module">The module, compiled on <see cref="Engine"/>.</param>
    /// <param name="request">What the guest sees and the envelope it runs in.</param>
    /// <returns>How the run ended and what it cost.</returns>
    /// <exception cref="IOException">The capture folder cannot be used (a host fault, not the guest's).</exception>
    /// <exception cref="InvalidOperationException">A preopened folder cannot be opened (a host fault, not the guest's).</exception>
    public WasmHostRunOutcome Run(WasmModule module, WasmHostRunRequest request)
    {
        // The host may live as long as the process, its capture folder in the host's temp
        // storage. A cloud client sweeps that storage whenever it reloads its controllers on an
        // idle node (every controller check, every reconnect), so the folder may be gone between
        // two runs.
        Directory.CreateDirectory(request.CaptureDirectory);

        var stdoutPath = Path.Combine(request.CaptureDirectory, $"{Guid.NewGuid():N}.out");
        var stderrPath = stdoutPath + ".err";
        var stdinPath = request.Stdin == null ? null : stdoutPath + ".in";
        if (stdinPath != null)
            File.WriteAllText(stdinPath, request.Stdin);

        var store = new Store(m_engine);
        var storeDisposed = false;
        try
        {
            store.SetWasiConfiguration(Wasi(request, stdinPath, stdoutPath, stderrPath));
            WasmHostLimits.Apply(store, request.Fuel, request.MemoryBytes, request.WallMs);

            // With the budget given after instantiation, instantiation gets the budget and what
            // copying segments can cost, whatever the platform charges for it.
            if (request.FuelAfterInstantiation)
                store.Fuel = (ulong)WasmHostLimits.InstantiationFuel(request.Fuel, request.MemoryBytes);

            using var linker = WasmHostLinker.Create(m_engine, request.RandomSeed);
            var exit = WasmHostExit.Define(linker);

            var start = DateTime.UtcNow;
            var refuel = request.FuelAfterInstantiation ? request.Fuel : (long?)null;
            var invocation = WasmHostGuestThread.Run(() => Invoke(linker, store, module, exit, refuel));
            var wallMs = (int)(DateTime.UtcNow - start).TotalMilliseconds;

            var fuelConsumed = request.FuelAfterInstantiation
                ? Math.Max(0, Math.Max(1, request.Fuel) - (long)Math.Min(store.Fuel, long.MaxValue))
                : (long)((ulong)request.Fuel - store.Fuel);

            // Dispose the store BEFORE reading — wasmtime holds OS handles on the stdio
            // redirect files until then.
            store.Dispose();
            storeDisposed = true;

            var (stdout, stdoutTruncated) = ReadCapped(stdoutPath, request.StdoutLimitBytes);
            var (stderr, stderrTruncated) = ReadCapped(stderrPath, request.StderrLimitBytes);

            // A module that could not be instantiated never ran, and a trap ends a run without a
            // word from the program: when it said nothing, the host's reason is its stderr.
            if (invocation.Fault != null && stderr.Length == 0)
                stderr = invocation.Fault;

            // An allocation past the memory cap usually surfaces as the guest's own failure (a
            // clean nonzero exit, an abort), not a trap of the engine: the caller's test of the
            // guest's words tells it apart.
            var verdict = invocation.Verdict;
            if (request.OutOfMemory != null && IsFailedEnd(invocation) && request.OutOfMemory(stderr))
                verdict = WasmHostVerdict.OutOfMemory;

            return new WasmHostRunOutcome(verdict, invocation.ExitCode, invocation.Trap, invocation.Fault,
                stdout, stdoutTruncated, stderr, stderrTruncated, fuelConsumed, invocation.PeakMemory, wallMs, invocation.EngineError);
        }
        finally
        {
            if (!storeDisposed)
                store.Dispose(); // releases wasmtime's handles on the stdio files
            TryDelete(stdoutPath);
            TryDelete(stderrPath);
            if (stdinPath != null)
                TryDelete(stdinPath);
        }
    }

    private static WasiConfiguration Wasi(WasmHostRunRequest request, string? stdinPath, string stdoutPath, string stderrPath)
    {
        var wasi = new WasiConfiguration().WithArgs(request.Argv);

        foreach (var (name, value) in request.Environment)
            wasi = wasi.WithEnvironmentVariable(name, value);

        foreach (var preopen in request.Preopens)
            wasi = wasi.WithPreopenedDirectory(preopen.HostPath, preopen.GuestPath, preopen.Writable);

        if (stdinPath != null)
            wasi = wasi.WithStandardInput(stdinPath);

        return wasi.WithStandardOutput(stdoutPath).WithStandardError(stderrPath);
    }

    private static Invocation Invoke(Linker linker, Store store, WasmModule module, WasmHostExit exit, long? refuel)
    {
        Instance instance;
        try
        {
            instance = linker.Instantiate(store, module);
        }
        catch (WasmtimeException) when (exit.Status is { } status)
        {
            // A guest that exits during module init (a start function, before _start) lands here.
            return new(WasmHostVerdict.Exited, status, null, 0);
        }
        catch (TrapException trap)
        {
            return Trapped(trap, 0);
        }
        catch (WasmtimeException exception)
        {
            // Unresolved imports, a type mismatch against WASI, a memory the store cannot
            // grant: the module never ran.
            return new(WasmHostVerdict.NotInstantiated, -1, null, 0, $"module failed to instantiate: {exception.Message}");
        }

        // The budget again, now that the data segments are in place: what instantiation took
        // depends on the platform (a copy-on-write image costs nothing, a copy costs a unit per
        // byte), what the guest takes does not.
        if (refuel is { } fuel)
            store.Fuel = (ulong)Math.Max(1, fuel);

        try
        {
            var startAction = instance.GetAction(START_EXPORT);
            if (startAction == null)
                return new(WasmHostVerdict.NoStart, -1, null, PeakMemory(instance), $"module exports no {START_EXPORT}");

            startAction();
            return new(WasmHostVerdict.Exited, 0, null, PeakMemory(instance));
        }
        catch (WasmtimeException) when (exit.Status is { } status)
        {
            // proc_exit, the host's own (WasmHostExit): a clean guest exit with its status (0 =
            // success), 126 and more included.
            return new(WasmHostVerdict.Exited, status, null, PeakMemory(instance));
        }
        catch (TrapException trap)
        {
            return Trapped(trap, PeakMemory(instance));
        }
        catch (WasmtimeException exception)
        {
            // Neither an exit nor a trap: the engine failed one of the guest's calls. None is known
            // since proc_exit is the host's own (wasmtime 48's refused a status of 126 or more).
            return new(WasmHostVerdict.EngineError, -1, null, PeakMemory(instance), ENGINE_ERROR_FAULT, exception);
        }
    }

    private static Invocation Trapped(TrapException trap, long peakMemory)
    {
        var verdict = trap.Type switch
        {
            TrapCode.OutOfFuel => WasmHostVerdict.OutOfFuel,
            TrapCode.Interrupt => WasmHostVerdict.Interrupted,
            // Anything else is a crash, an access outside the memory included: a wild pointer or
            // a stack that overflowed is a fault of the program, not a refusal of memory.
            _ => WasmHostVerdict.Trap
        };

        return new(verdict, -1, trap.Type, peakMemory, WasmHostTraps.Fault(trap.Type));
    }

    private static bool IsFailedEnd(Invocation invocation)
    {
        return invocation.Verdict switch
        {
            WasmHostVerdict.Exited => invocation.ExitCode != 0,
            WasmHostVerdict.Trap or WasmHostVerdict.NoStart => true,
            _ => false
        };
    }

    private static long PeakMemory(Instance instance)
    {
        // WASM linear memory only grows, so its current length is the peak.
        try
        {
            return instance.GetMemory(GUEST_MEMORY)?.GetLength() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static (string Text, bool Truncated) ReadCapped(string path, int capBytes)
    {
        if (!File.Exists(path))
            return ("", false);

        var bytes = File.ReadAllBytes(path);
        if (capBytes > 0 && bytes.Length > capBytes)
            return (Encoding.UTF8.GetString(bytes, 0, capBytes), true);

        return (Encoding.UTF8.GetString(bytes), false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // a capture file; best-effort
        }
    }

    private void AdvanceEpoch(object? state)
    {
        m_engine.IncrementEpoch();
    }

    #endregion

    #region IDisposable

    /// <summary>Stops the epoch timer and releases the engine. Modules compiled on it must be disposed first.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_epochTimer.Dispose();
        m_engine.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>The engine every store of this host shares; compile modules on it.</summary>
    public WasmEngine Engine => m_engine;

    /// <summary>
    /// Whether a guest's traps are explicit checks in this process: true where the host enforces
    /// hardware stack protection (a hardware trap could not be resumed from), false elsewhere.
    /// </summary>
    public bool ExplicitTraps => m_explicitTraps;

    #endregion

    #region Nested Types

    /// <summary>What one instantiate-and-start attempt came to; Fault is the host's reason when the guest could not say it.</summary>
    private sealed record Invocation(WasmHostVerdict Verdict, int ExitCode, TrapCode? Trap, long PeakMemory, string? Fault = null, WasmtimeException? EngineError = null);

    #endregion
}
