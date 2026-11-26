namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// The exit codes of the tool, one meaning each: a caller tells a difference from a failure, a
/// stop and an error by the code alone.
/// </summary>
internal static class SmtLiveExitCodes
{
    #region Constants

    /// <summary>Everything that was asked for was done and held.</summary>
    public const int OK = 0;

    /// <summary>
    /// The work was done and did not hold: a job did not complete, a document did not come or
    /// does not hold to its schema, a preflight would not start or refuses the options, a file
    /// of a pack was skipped or its row refused, a local run had no sandbox.
    /// </summary>
    public const int FAILED = 1;

    /// <summary>The command line, an input, the folder or the environment is wrong; nothing was sent or run.</summary>
    public const int USAGE = 2;

    /// <summary>A run completed and its documents hold to their schemas, but they report something a lab must look at.</summary>
    public const int ALARM = 3;

    /// <summary>Two runs differ (<c>compare</c>, <c>comparative</c>).</summary>
    public const int DIFFER = 4;

    /// <summary>Stopped by Ctrl+C; what was stopped, and what goes on on the server, is said.</summary>
    public const int STOPPED = 5;

    /// <summary>
    /// The tool could not finish: the connection, the server or this machine failed (a sign-in,
    /// an upload, the waiting for a job, a download, a file); what is left on the server is said.
    /// </summary>
    public const int ERROR = 6;

    #endregion
}
