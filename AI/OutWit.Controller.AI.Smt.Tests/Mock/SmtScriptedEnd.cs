namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// How the run of a scripted solver ended (<see cref="SmtSessionScript"/>): what the sandbox
/// would report of it. The default is a solver that ended by itself with status 0.
/// </summary>
internal sealed class SmtScriptedEnd
{
    #region Properties

    /// <summary>The sandbox's verdict, by its name: <c>Exited</c>, <c>Trap</c>, <c>OutOfFuel</c>, <c>Interrupted</c>, <c>OutOfMemory</c>, <c>NotInstantiated</c>, <c>NoStart</c>, <c>EngineError</c>, <c>OutputLimit</c>.</summary>
    public string Verdict { get; init; } = "Exited";

    /// <summary>The solver's exit status.</summary>
    public int ExitCode { get; init; }

    /// <summary>The sandbox's fixed words for the end, or null.</summary>
    public string? Fault { get; init; }

    /// <summary>The message of the engine's own exception, for a verdict <c>EngineError</c>; null for none.</summary>
    public string? EngineError { get; init; }

    /// <summary>Whether the solver started at all; false for a module that was never run, which reads nothing.</summary>
    public bool Started { get; init; } = true;

    /// <summary>What the solver wrote to stdout before its first read.</summary>
    public string Before { get; init; } = "";

    /// <summary>What the solver wrote to stdout after its last read.</summary>
    public string Rest { get; init; } = "";

    /// <summary>What the solver wrote to stderr.</summary>
    public string Stderr { get; init; } = "";

    /// <summary>The fuel the run used.</summary>
    public long Fuel { get; init; } = 1_000;

    /// <summary>The solver's linear memory at the end.</summary>
    public long PeakMemoryBytes { get; init; } = 65_536;

    #endregion
}
