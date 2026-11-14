namespace OutWit.Controller.Smt.Spike.Protocol;

/// <summary>
/// How one solver session ended, wasm or native: the engine's end (an exit code, a trap, out of
/// fuel, the wall clock), what the solver wrote, and what it cost. The answers themselves are in
/// the session's <see cref="SmtSessionDriver"/>.
/// </summary>
internal sealed class SmtSessionOutcome
{
    #region Properties

    /// <summary>
    /// exited, trap, out_of_fuel, interrupted (wall clock), not_instantiated, no_start, engine_error,
    /// output_exceeded, timeout (native), start_failed (native).
    /// </summary>
    public required string End { get; init; }

    /// <summary>The exit code when the solver exited, else -1.</summary>
    public int ExitCode { get; init; } = -1;

    /// <summary>The trap's fixed words for a crash, else null.</summary>
    public string? Fault { get; init; }

    /// <summary>The solver's whole stdout.</summary>
    public string Stdout { get; init; } = "";

    /// <summary>The solver's stderr (capped).</summary>
    public string Stderr { get; init; } = "";

    /// <summary>The guest's fuel from its first instruction to its end; 0 for native.</summary>
    public long Fuel { get; init; }

    /// <summary>The fuel instantiation took (the platform's term: data segments copied, or none with a copy-on-write image).</summary>
    public long InstantiationFuel { get; init; }

    /// <summary>The guest's fuel up to its first request for input (the solver's own start-up).</summary>
    public long StartupFuel { get; init; }

    /// <summary>The guest's linear memory at the end (its peak); for native the peak working set when known.</summary>
    public long PeakMemoryBytes { get; init; }

    /// <summary>Milliseconds from the start of the session (store or process) to its end.</summary>
    public long WallMs { get; init; }

    /// <summary>Milliseconds to make the instance (store, linker, instantiation); 0 for native.</summary>
    public double InstantiateMs { get; init; }

    /// <summary>Milliseconds from the start until the solver first asked for input (wasm) .</summary>
    public double StartupMs { get; init; }

    #endregion
}
