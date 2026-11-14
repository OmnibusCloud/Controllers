using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Wasm;
using OutWit.Controller.Smt.Spike.Protocol;
using Wasmtime;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.Smt.Spike.Wasm;

/// <summary>
/// One interactive Z3 session in the shared host's engine: a fresh store with the host's
/// envelope (<see cref="WasmHostLimits"/>), the host's deterministic linker
/// (<see cref="WasmHostLinker"/>: pinned clock, seeded randomness) with stdin and stdout as host
/// calls (<see cref="SmtGuestStdio"/>), the guest on the host's big-stack thread
/// (<see cref="WasmHostGuestThread"/>).
/// <para>
/// Two things differ from <see cref="WasmHost.Run"/>, and both are what the spike proposes for
/// the host: the guest's stdin is a conversation, not a file; and the fuel budget is given after
/// instantiation, so the fuel a run reports is the guest's own on every platform (Windows pays
/// one unit per byte of data segments at instantiation, Linux's copy-on-write image none).
/// </para>
/// </summary>
internal sealed class SmtWasmSession
{
    #region Constants

    private const string START_EXPORT = "_start";
    private const string GUEST_MEMORY = "memory";

    // Fuel for instantiation alone (only Windows spends any: one unit per data byte); the
    // guest's own budget replaces it before its first instruction.
    private const ulong INSTANTIATION_FUEL = 1UL << 40;

    #endregion

    #region Fields

    private readonly WasmHost m_host;
    private readonly WasmModule m_module;

    #endregion

    #region Constructors

    /// <summary>Makes a session runner on a host and a module compiled on its engine.</summary>
    /// <param name="host">The shared host (its engine has wasm exceptions on in this spike).</param>
    /// <param name="module">z3.wasm, compiled on the host's engine.</param>
    public SmtWasmSession(WasmHost host, WasmModule module)
    {
        m_host = host;
        m_module = module;
    }

    #endregion

    #region Functions

    /// <summary>Runs one session to its end.</summary>
    /// <param name="argv">The solver's command line, argv[0] included.</param>
    /// <param name="driver">The protocol driver.</param>
    /// <param name="fuel">The guest's fuel budget (after instantiation).</param>
    /// <param name="memoryBytes">The cap of the guest's linear memory.</param>
    /// <param name="wallMs">The wall-clock backstop.</param>
    /// <param name="seed">The seed of the guest's random_get.</param>
    /// <param name="stdoutLimit">The most stdout bytes kept.</param>
    /// <returns>How the session ended.</returns>
    public SmtSessionOutcome Run(IReadOnlyList<string> argv, SmtSessionDriver driver, long fuel, long memoryBytes, int wallMs, long seed, int stdoutLimit)
    {
        var clock = Stopwatch.StartNew();
        using var store = new Store(m_host.Engine);
        store.SetWasiConfiguration(new WasiConfiguration().WithArgs(argv));
        WasmHostLimits.Apply(store, (long)INSTANTIATION_FUEL, memoryBytes, wallMs);

        using var linker = WasmHostLinker.Create(m_host.Engine, seed);
        var stdio = new SmtGuestStdio(driver, fuel, stdoutLimit, 64 << 10) { Clock = clock };
        stdio.Define(linker);

        var result = WasmHostGuestThread.Run(() => Invoke(linker, store, fuel, clock));
        var stdout = Encoding.UTF8.GetString(stdio.Stdout);
        var end = stdio.OutputExceeded && result.End == "exited" ? "output_exceeded" : result.End;

        return new SmtSessionOutcome
        {
            End = end,
            ExitCode = result.ExitCode,
            Fault = result.Fault,
            Stdout = stdout,
            Stderr = stdio.Stderr,
            Fuel = result.Fuel,
            InstantiationFuel = result.InstantiationFuel,
            StartupFuel = stdio.FuelAtFirstRead,
            PeakMemoryBytes = result.PeakMemory,
            WallMs = clock.ElapsedMilliseconds,
            InstantiateMs = result.InstantiateMs,
            StartupMs = stdio.MsAtFirstRead
        };
    }

    private Invocation Invoke(Linker linker, Store store, long fuel, Stopwatch clock)
    {
        Instance instance;
        try
        {
            instance = linker.Instantiate(store, m_module);
        }
        catch (TrapException trap)
        {
            return new Invocation(EndOf(trap), -1, WasmHostTraps.Fault(trap.Type), 0, 0, 0, clock.Elapsed.TotalMilliseconds);
        }
        catch (WasmtimeException exception)
        {
            return new Invocation("not_instantiated", -1, exception.Message, 0, 0, 0, clock.Elapsed.TotalMilliseconds);
        }

        var instantiationFuel = (long)(INSTANTIATION_FUEL - store.Fuel);
        var instantiateMs = clock.Elapsed.TotalMilliseconds;

        // The guest's budget, from its first instruction: the same number on every platform.
        store.Fuel = (ulong)Math.Max(1, fuel);

        var start = instance.GetAction(START_EXPORT);
        if (start == null)
            return new Invocation("no_start", -1, $"module exports no {START_EXPORT}", 0, instantiationFuel, 0, instantiateMs);

        string end;
        var exitCode = -1;
        string? fault = null;
        try
        {
            start();
            end = "exited";
            exitCode = 0;
        }
        catch (WasmtimeException exception) when (exception.ExitCode is { } code)
        {
            end = "exited";
            exitCode = code;
        }
        catch (TrapException trap)
        {
            end = EndOf(trap);
            fault = WasmHostTraps.Fault(trap.Type);
        }
        catch (WasmtimeException exception)
        {
            end = "engine_error";
            fault = exception.Message;
        }

        var used = fuel - (long)store.Fuel;
        return new Invocation(end, exitCode, fault, used, instantiationFuel, PeakMemory(instance), instantiateMs);
    }

    private static string EndOf(TrapException trap)
    {
        return trap.Type switch
        {
            TrapCode.OutOfFuel => "out_of_fuel",
            TrapCode.Interrupt => "interrupted",
            _ => "trap"
        };
    }

    private static long PeakMemory(Instance instance)
    {
        try
        {
            return instance.GetMemory(GUEST_MEMORY)?.GetLength() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region Nested Types

    private sealed record Invocation(string End, int ExitCode, string? Fault, long Fuel, long InstantiationFuel, long PeakMemory, double InstantiateMs);

    #endregion
}
