namespace OutWit.Controller.CalculiX.Runtime;

/// <summary>
/// Outcome of one ccx run: forwarded exit code, measured wall time and the
/// tail of the merged output stream (the error tail of a failed solve).
/// </summary>
public sealed class CcxRunOutcome
{
    #region Constructors

    /// <summary>
    /// Captures one finished run.
    /// </summary>
    /// <param name="exitCode">Solver process exit code.</param>
    /// <param name="elapsedSeconds">Measured wall-clock duration in seconds.</param>
    /// <param name="logTail">Last lines of merged stdout/stderr.</param>
    public CcxRunOutcome(int exitCode, double elapsedSeconds, string logTail)
    {
        ExitCode = exitCode;
        ElapsedSeconds = elapsedSeconds;
        LogTail = logTail;
    }

    #endregion

    #region Properties

    /// <summary>Solver process exit code; 0 = success.</summary>
    public int ExitCode { get; }

    /// <summary>Measured wall-clock duration in seconds.</summary>
    public double ElapsedSeconds { get; }

    /// <summary>Last lines of merged stdout/stderr.</summary>
    public string LogTail { get; }

    #endregion
}
