using System.Runtime.CompilerServices;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The wasm sandbox host a controller carries as its own compiled-in copy: one wasmtime engine
/// with the deterministic configuration (<see cref="WasmHostEngineConfig"/>), one thread that
/// advances its epoch, and the run of one WASI command module in a fresh store within a fuel /
/// memory / wall-clock envelope, mapped to a <see cref="WasmHostVerdict"/>. Modules are compiled
/// on <see cref="Engine"/> by the caller and may be run many times, concurrently, from any thread.
/// <para>
/// The host refuses to start, with a message naming both versions, when the loaded binding is not
/// the one the controller was built against (<see cref="WasmHostBinding"/>), and with a message
/// naming both hashes when the native library the binding loaded is not the one the controllers
/// ship (<see cref="WasmHostNatives"/>); nothing of it starts then, and no run can fail for that
/// reason later.
/// </para>
/// <para>
/// A module compiled on <see cref="Engine"/> is machine code of this library and of this trap mode;
/// a cache that keeps compiled code past the engine keys it by <see cref="CompiledCodeKey"/>.
/// </para>
/// </summary>
internal sealed class WasmHost : IDisposable
{
    #region Constants

    private const string GUEST_MEMORY = "memory";
    private const string START_EXPORT = "_start";

    /// <summary>The fault of a run the engine ended without a trap or an exit code.</summary>
    public const string ENGINE_ERROR_FAULT = "wasm error: the engine refused a call of the guest";

    /// <summary>The fault of a run that one of the host's own calls could not serve.</summary>
    public const string HOST_FAULT = "the host failed inside a call of the guest";

    private const string EPOCH_THREAD_NAME = "wasm-epoch";

    private const string EXPLICIT_TRAPS = "explicit-traps";
    private const string SIGNAL_TRAPS = "signal-traps";

    #endregion

    #region Fields

    private readonly WasmEngine m_engine;
    private readonly WasmHostShim m_shim;
    private readonly Thread m_epochThread;
    private readonly ManualResetEventSlim m_stopped = new(false);
    private readonly bool m_explicitTraps;
    private readonly string m_compiledCodeKey;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a host whose engine compiles a module's functions on several threads at once: for
    /// modules the controller ships.
    /// </summary>
    /// <exception cref="InvalidOperationException">The loaded binding is not the one built against, the native library is not the pinned one or none was found, or the binding lacks what the engine settings need.</exception>
    public WasmHost()
        : this(parallelCompilation: true)
    {
    }

    /// <summary>
    /// Creates a host: the binding guard first, then the module-aware native resolution (before
    /// the first wasmtime P/Invoke: in the shared load context the cloud client uses, the native
    /// library is found only through it), then the natives guard on the library the binding
    /// loaded, then one engine, its shim and the thread that advances its epoch.
    /// </summary>
    /// <param name="parallelCompilation">
    /// Whether the engine compiles a module's functions on several threads of its own. Off, a
    /// compile takes the calling thread and no other: for modules a task brought, whose compiling
    /// must stay within the cores the caller counted.
    /// </param>
    /// <exception cref="InvalidOperationException">The loaded binding is not the one built against, the native library is not the pinned one or none was found, or the binding lacks what the engine settings need.</exception>
    public WasmHost(bool parallelCompilation)
    {
        WasmHostBinding.ThrowIfUnavailable();
        WasmHostNativeLibrary.EnsureRegistered();

        m_engine = CreateEngine(parallelCompilation, out m_explicitTraps);
        m_compiledCodeKey = CompiledCodeKeyOf(WasmHostNatives.Sha256Of(WasmHostNativeLibrary.LoadedPath!) ?? "", m_explicitTraps);
        m_shim = new WasmHostShim(m_engine);

        // One thread of the host's own advances the epoch for every store; per-store deadlines
        // (SetEpochDeadline) convert a wall-time budget into a tick count. Not a pool timer: a
        // pool that is busy or starved would stretch every wall limit of the process.
        m_epochThread = new Thread(AdvanceEpochs)
        {
            IsBackground = true,
            Name = EPOCH_THREAD_NAME,
            Priority = ThreadPriority.AboveNormal
        };
        m_epochThread.Start();
    }

    /// <summary>
    /// Makes the engine. Kept out of line, so that every member of the binding the engine needs is
    /// looked up after the guard has passed, not when the constructor is compiled.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WasmEngine CreateEngine(bool parallelCompilation, out bool explicitTraps)
    {
        return new WasmEngine(WasmHostEngineConfig.Create(parallelCompilation, out explicitTraps));
    }

    #endregion

    #region Functions

    /// <summary>
    /// The key compiled code of an engine is valid for: the SHA-256 of the native library that
    /// compiled it and the trap mode it was compiled for. A module compiled by one library and
    /// loaded by another can deserialize without error and then trap (wasmtime 48.0.8 and 48.0.2),
    /// and code compiled for hardware traps has no explicit checks.
    /// </summary>
    /// <param name="nativesSha256">The native library's SHA-256.</param>
    /// <param name="explicitTraps">Whether traps are explicit checks.</param>
    /// <returns>The key, e.g. <c>wasmtime-&lt;sha256&gt;-explicit-traps</c>.</returns>
    public static string CompiledCodeKeyOf(string nativesSha256, bool explicitTraps)
    {
        return $"wasmtime-{nativesSha256}-{(explicitTraps ? EXPLICIT_TRAPS : SIGNAL_TRAPS)}";
    }

    /// <summary>
    /// Runs one WASI command module once, in its own store: the request's argv, environment,
    /// folders and stdin, the deterministic clock and randomness, the fuel / memory / wall-clock
    /// envelope; stdout and stderr are the host's own streams, capped at the write
    /// (<see cref="WasmHostSyscalls"/>). Runs on a thread that holds the guest's stack
    /// (<see cref="WasmHostGuestThread"/>). Never throws for a guest-side fault: every way the
    /// guest can end is a verdict.
    /// </summary>
    /// <param name="module">The module, compiled on <see cref="Engine"/>.</param>
    /// <param name="request">What the guest sees and the envelope it runs in.</param>
    /// <returns>How the run ended and what it cost.</returns>
    /// <exception cref="IOException">The capture folder or the file stdout goes to cannot be used, or one of the host's own calls failed (a host fault, not the guest's).</exception>
    /// <exception cref="InvalidOperationException">A preopened folder cannot be opened (a host fault, not the guest's).</exception>
    /// <exception cref="ArgumentException">Some of the run's folders have a table of names and some have none.</exception>
    public WasmHostRunOutcome Run(WasmModule module, WasmHostRunRequest request)
    {
        // The host may live as long as the process, its capture folder in the host's temp
        // storage. A cloud client sweeps that storage whenever it reloads its controllers on an
        // idle node (every controller check, every reconnect), so the folder may be gone between
        // two runs.
        Directory.CreateDirectory(request.CaptureDirectory);

        // Descriptors 1 and 2 stay what they always were for WASI (files of the run), so that a
        // program sees the same kind of stream; nothing is written to them, since every write
        // to them is the host's own.
        var stdoutPath = Path.Combine(request.CaptureDirectory, $"{Guid.NewGuid():N}.out");
        var stderrPath = stdoutPath + ".err";
        var stdinPath = request.Stdin == null ? null : stdoutPath + ".in";
        if (stdinPath != null)
            File.WriteAllText(stdinPath, request.Stdin);

        WasmHostSyscalls? syscalls = null;
        var store = new Store(m_engine);
        var storeDisposed = false;
        try
        {
            syscalls = new WasmHostSyscalls(m_engine, m_shim, request);

            store.SetWasiConfiguration(Wasi(request, stdinPath, stdoutPath, stderrPath));
            WasmHostLimits.Apply(store, request.Fuel, request.MemoryBytes, request.WallMs);

            // With the budget given after instantiation, instantiation gets the budget and what
            // copying segments can cost, whatever the platform charges for it.
            if (request.FuelAfterInstantiation)
                store.Fuel = (ulong)WasmHostLimits.InstantiationFuel(request.Fuel, request.MemoryBytes);

            using var linker = WasmHostLinker.Create(m_engine);
            syscalls.Define(linker);
            var exit = WasmHostExit.Define(linker);

            var start = DateTime.UtcNow;
            var refuel = request.FuelAfterInstantiation ? request.Fuel : (long?)null;
            var calls = syscalls;
            var invocation = WasmHostGuestThread.Run(() => Invoke(linker, store, module, exit, calls, refuel));
            var wallMs = (int)(DateTime.UtcNow - start).TotalMilliseconds;

            var fuelConsumed = request.FuelAfterInstantiation
                ? Math.Max(0, Math.Max(1, request.Fuel) - (long)Math.Min(store.Fuel, long.MaxValue))
                : (long)((ulong)request.Fuel - store.Fuel);

            // Dispose the store before the files go: wasmtime holds OS handles on the stdio
            // redirect files until then.
            store.Dispose();
            storeDisposed = true;

            // One of the host's own calls failed (a full disk under the file stdout goes to): the
            // run says nothing about the program.
            if (syscalls.Fault != null)
                throw new IOException(HOST_FAULT, syscalls.Fault);

            var stdout = syscalls.Stdout.Text();
            var stderr = syscalls.Stderr.Text();

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
                stdout, syscalls.Stdout.Cut, stderr, syscalls.Stderr.Cut, fuelConsumed, invocation.PeakMemory, wallMs, invocation.EngineError, syscalls.Stdout.Kept);
        }
        finally
        {
            if (!storeDisposed)
                store.Dispose(); // releases wasmtime's handles on the stdio files
            syscalls?.Dispose();
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

    private static Invocation Invoke(Linker linker, Store store, WasmModule module, WasmHostExit exit, WasmHostSyscalls syscalls, long? refuel)
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
        catch (WasmtimeException) when (syscalls.End is { } end)
        {
            return Ended(end, 0);
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
        catch (WasmtimeException) when (syscalls.End is { } end)
        {
            return Ended(end, PeakMemory(instance));
        }
        catch (TrapException trap)
        {
            return Trapped(trap, PeakMemory(instance));
        }
        catch (WasmtimeException exception)
        {
            // Neither an exit nor a trap: the engine failed one of the guest's calls. None is known:
            // proc_exit is the host's own (wasmtime 48's refused a status of 126 or more), and the
            // host's calls check a guest's pointers themselves.
            return new(WasmHostVerdict.EngineError, -1, null, PeakMemory(instance), ENGINE_ERROR_FAULT, exception);
        }
    }

    /// <summary>
    /// A run one of the host's own calls ended: out of fuel in a call that costs fuel (a budget
    /// stop like the engine's own), or stdout past its cap.
    /// </summary>
    private static Invocation Ended(WasmHostVerdict end, long peakMemory)
    {
        return new(end, -1, end == WasmHostVerdict.OutOfFuel ? TrapCode.OutOfFuel : null, peakMemory);
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

    private void AdvanceEpochs()
    {
        // A wait of one tick is as long as the operating system's timer makes it (some 15 ms on
        // Windows): the epoch follows the time that passed, not the number of wake-ups.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long ticks = 0;

        while (!m_stopped.Wait(WasmHostLimits.EPOCH_TICK_MS))
        {
            var due = clock.ElapsedMilliseconds / WasmHostLimits.EPOCH_TICK_MS;
            for (; ticks < due; ticks++)
                m_engine.IncrementEpoch();
        }
    }

    #endregion

    #region IDisposable

    /// <summary>Stops the epoch thread and releases the shim and the engine. Modules compiled on it must be disposed first.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_stopped.Set();
        m_epochThread.Join();
        m_stopped.Dispose();
        m_shim.Dispose();
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

    /// <summary>
    /// What a module compiled on <see cref="Engine"/> is valid for: this process's native library
    /// and trap mode (<see cref="CompiledCodeKeyOf"/>). Nothing of the host keeps compiled code past
    /// its engine; a cache that does keys its entries by this and loads nothing under another key.
    /// </summary>
    public string CompiledCodeKey => m_compiledCodeKey;

    #endregion

    #region Nested Types

    /// <summary>What one instantiate-and-start attempt came to; Fault is the host's reason when the guest could not say it.</summary>
    private sealed record Invocation(WasmHostVerdict Verdict, int ExitCode, TrapCode? Trap, long PeakMemory, string? Fault = null, WasmtimeException? EngineError = null);

    #endregion
}
