namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// How much a problem set may hold, and how much a reader of one keeps. The numbers are the
/// contract a lab writes to: the schema of a row and its description state the same ones.
/// </summary>
public static class SmtProblemSetLimits
{
    #region Constants

    /// <summary>The most bytes of one line, its line end not counted: 16 MiB. A longer line is refused without being held in memory.</summary>
    public const int MAX_LINE_BYTES = 16 * 1024 * 1024;

    /// <summary>The most rows of one set. The row after that is refused, and reading ends.</summary>
    public const int MAX_ROWS = 1_000_000;

    /// <summary>
    /// The most bytes of one set, every byte counted: its lines, their line ends, blank lines and
    /// a byte order mark. The line that would take the set past it is refused, once, and reading
    /// ends there. A server reads a set whole several times (the preflight, the split and the
    /// settlement each read it and sanitize every problem again): 4 GiB is a few minutes of that a
    /// pass, and under what the platform's blob store takes by default.
    /// </summary>
    public const long MAX_SET_BYTES = 4L * 1024 * 1024 * 1024;

    /// <summary>The most characters of an id.</summary>
    public const int MAX_ID_CHARACTERS = 64;

    /// <summary>The most terms whose values one row asks.</summary>
    public const int MAX_VALUES = 256;

    /// <summary>The most constants one row is projected on.</summary>
    public const int MAX_PROJECT = 256;

    /// <summary>The most characters of a row's tags, counted as a schema's <c>maxLength</c> counts them: Unicode code points.</summary>
    public const int MAX_TAGS_CHARACTERS = 256;

    /// <summary>The most refusal records a summary keeps; the refusals after them are only counted.</summary>
    public const int MAX_REFUSALS_KEPT = 1_000;

    /// <summary>The most distinct licences a summary keeps by name; the problems of any further licence are counted together.</summary>
    public const int MAX_LICENSES = 64;

    /// <summary>The most distinct logics a summary keeps by name; the problems of any further logic are counted together.</summary>
    public const int MAX_LOGICS = 64;

    #endregion
}
