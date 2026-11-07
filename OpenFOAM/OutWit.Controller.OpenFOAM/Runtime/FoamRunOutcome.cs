namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Outcome of one process run: forwarded exit code, measured wall time and
/// the tail of the merged output stream (the error tail of a failed step).
/// The full output is in the step's log file.
/// </summary>
public sealed class FoamRunOutcome
{
    #region Constructors

    /// <summary>
    /// Captures one finished run.
    /// </summary>
    /// <param name="exitCode">Process exit code.</param>
    /// <param name="elapsedSeconds">Measured wall-clock duration in seconds.</param>
    /// <param name="logTail">Last lines of merged stdout/stderr.</param>
    public FoamRunOutcome(int exitCode, double elapsedSeconds, string logTail)
    {
        ExitCode = exitCode;
        ElapsedSeconds = elapsedSeconds;
        LogTail = logTail;
    }

    #endregion

    #region Properties

    /// <summary>Process exit code; 0 = success.</summary>
    public int ExitCode { get; }

    /// <summary>Measured wall-clock duration in seconds.</summary>
    public double ElapsedSeconds { get; }

    /// <summary>Last lines of merged stdout/stderr.</summary>
    public string LogTail { get; }

    #endregion
}
