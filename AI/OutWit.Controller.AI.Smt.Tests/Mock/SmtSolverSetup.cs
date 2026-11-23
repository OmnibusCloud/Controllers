namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// What a test changes of a session that a run's options cannot: the sandbox it runs in, and
/// single numbers of its envelope. Everything left as it is comes from the options.
/// </summary>
internal sealed class SmtSolverSetup
{
    #region Properties

    /// <summary>Runs the session in a sandbox whose traps are explicit checks, instead of the process's own sandbox.</summary>
    public bool ExplicitTraps { get; init; }

    /// <summary>Runs the session as a process without a sandbox would, with these words for the reason; null for a process that has one.</summary>
    public string? WithoutSandbox { get; init; }

    /// <summary>The session's output cap, bytes; null for the profile's.</summary>
    public int? OutputLimitBytes { get; init; }

    /// <summary>The session's wall-clock backstop, milliseconds; null for the profile's.</summary>
    public int? BackstopMs { get; init; }

    /// <summary>The session's fuel; null for what the options' hard cap gives.</summary>
    public long? Fuel { get; init; }

    /// <summary>The host's cap of the solver's linear memory, bytes; null for the profile's rule.</summary>
    public long? HostMemoryBytes { get; init; }

    #endregion
}
