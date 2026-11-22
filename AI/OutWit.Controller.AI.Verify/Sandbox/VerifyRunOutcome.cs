using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>One guest invocation's outcome, before suite evaluation.</summary>
/// <param name="Verdict">The run's verdict; without meaning when <paramref name="Fault"/> is set.</param>
/// <param name="ExitCode">The exit code; -1 for a run that did not end by itself.</param>
/// <param name="Exited">Whether the program ended by itself: it returned from its entry or called exit, with any status. A crash, a budget stop and a module that never ran did not.</param>
/// <param name="Stdout">Captured stdout, capped.</param>
/// <param name="StdoutTruncated">Whether stdout hit its cap.</param>
/// <param name="StdoutBytes">The bytes of stdout kept: what the run took from its task's stdout budget.</param>
/// <param name="Stderr">Captured stderr, capped.</param>
/// <param name="StderrTruncated">Whether stderr hit its cap.</param>
/// <param name="FuelConsumed">Fuel consumed; the budget for a budget stop or a crash.</param>
/// <param name="PeakMemoryBytes">Peak linear memory.</param>
/// <param name="WallMs">Wall time of the invocation.</param>
/// <param name="Fault">Null for a run with a verdict; else the reason code (<see cref="VerifyFault"/>) of a run the node could not carry to an end of its own.</param>
internal sealed record VerifyRunOutcome(
    VerifyVerdict Verdict, int ExitCode, bool Exited,
    string Stdout, bool StdoutTruncated, int StdoutBytes, string Stderr, bool StderrTruncated,
    long FuelConsumed, long PeakMemoryBytes, int WallMs, string? Fault = null)
{
    #region Constants

    /// <summary>The stdout cap of a run that may keep nothing: its task's budget is spent.</summary>
    public const int NO_STDOUT = -1;

    #endregion

    #region Functions

    /// <summary>A run the node could not carry to an end of its own.</summary>
    /// <param name="fault">The reason code.</param>
    /// <param name="wallMs">The wall time it took.</param>
    /// <returns>The outcome.</returns>
    public static VerifyRunOutcome Faulted(string fault, int wallMs)
    {
        return new VerifyRunOutcome(VerifyVerdict.NoResult, -1, false, "", false, 0, "", false, 0, 0, wallMs, fault);
    }

    #endregion
}
