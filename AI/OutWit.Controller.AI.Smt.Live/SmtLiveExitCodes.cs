namespace OutWit.Controller.AI.Smt.Live;

/// <summary>The exit codes of the tool.</summary>
internal static class SmtLiveExitCodes
{
    #region Constants

    /// <summary>Everything that was asked for was done and held.</summary>
    public const int OK = 0;

    /// <summary>
    /// A job did not complete, a document is missing or does not hold to its schema, a preflight
    /// is not runnable, two runs differ, or a row of a packed set is refused.
    /// </summary>
    public const int FAILED = 1;

    /// <summary>The command line, an input file or the environment is wrong; nothing was sent.</summary>
    public const int USAGE = 2;

    /// <summary>A run completed and its documents hold to their schemas, but they report something a lab must look at.</summary>
    public const int ALARM = 3;

    #endregion
}
