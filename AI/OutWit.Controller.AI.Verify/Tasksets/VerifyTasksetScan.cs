namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// What one pass over a taskset found: the counts, the runtimes it names, and everything Split
/// refuses a taskset for, each kind of message bounded in number and length. It holds nothing
/// that grows with the taskset but those counts.
/// </summary>
public sealed class VerifyTasksetScan
{
    #region Constants

    /// <summary>What the last line of the clamping notes calls the ones it does not show.</summary>
    public const string NOTES = "note(s) of clamped limits";

    /// <summary>What the last line of the planning errors calls the ones it does not show.</summary>
    public const string UNPLANNED = "task(s) that cannot be planned";

    /// <summary>What a preflight says of unknown runtime ids past the ones it lists.</summary>
    public const string MORE_UNKNOWN_RUNTIMES = "more runtime ids are not known to this build than are listed here";

    // An unknown id is counted apart from a known one it may be shown like.
    private const string UNKNOWN_PREFIX = "?";

    #endregion

    #region Fields

    private readonly HashSet<string> m_named = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_unknown = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>
    /// Notes a runtime id a task names: listed once, an unknown one as a message may show it and
    /// only while the lists have room.
    /// </summary>
    /// <param name="runtimeId">The id.</param>
    /// <param name="known">Whether the controller has the runtime.</param>
    /// <returns>The name the task's batches are counted under, or null for an unknown id that is not listed.</returns>
    public string? Name(string runtimeId, bool known)
    {
        if (known)
        {
            if (m_named.Add(runtimeId))
                RuntimeIds.Add(runtimeId);

            return runtimeId;
        }

        UnknownTasks++;

        var shown = VerifyTasksetChecks.Shown(runtimeId);
        if (m_unknown.Contains(shown))
            return UNKNOWN_PREFIX + shown;

        if (UnknownRuntimeIds.Count >= VerifyTasksetLimits.MAX_LISTED_RUNTIMES)
        {
            MoreUnknownRuntimes = true;
            return null;
        }

        m_unknown.Add(shown);
        if (m_named.Add(shown))
            RuntimeIds.Add(shown);

        UnknownRuntimeIds.Add(shown);
        return UNKNOWN_PREFIX + shown;
    }

    #endregion

    #region Properties

    /// <summary>The lines that parsed into tasks.</summary>
    public int Tasks { get; set; }

    /// <summary>The tasks that are planned: those of a known runtime whose text fits a package.</summary>
    public int Planned { get; set; }

    /// <summary>
    /// The batches the tasks make: those that are planned, and those of the unknown runtimes that
    /// are listed (a taskset that names one is refused; the count is what it would have been).
    /// </summary>
    public int Batches { get; set; }

    /// <summary>The characters of source text, all tasks together.</summary>
    public long InputBytes { get; set; }

    /// <summary>Whether a task or the options set a wall time, which is no limit.</summary>
    public bool SetsWallTime { get; set; }

    /// <summary>The malformed lines, one message each.</summary>
    public VerifyMessageList Errors { get; } = new();

    /// <summary>The limits and the batch size that were clamped.</summary>
    public VerifyMessageList Notes { get; } = new();

    /// <summary>The tasks that cannot be planned.</summary>
    public VerifyMessageList PlanErrors { get; } = new();

    /// <summary>The runtime ids the taskset names, in first-seen order: the known ones as they are, the unknown ones as a message may show them, while the list has room.</summary>
    public List<string> RuntimeIds { get; } = [];

    /// <summary>The ids the controller does not have, as a message may show them, while the list has room.</summary>
    public List<string> UnknownRuntimeIds { get; } = [];

    /// <summary>The tasks that name a runtime the controller does not have.</summary>
    public int UnknownTasks { get; private set; }

    /// <summary>Whether the taskset names more unknown runtime ids than are listed.</summary>
    public bool MoreUnknownRuntimes { get; private set; }

    /// <summary>Whether Split takes the taskset: every line parsed, every runtime is known, every task fits, and there is a task.</summary>
    public bool Accepted => Tasks > 0 && Errors.Count == 0 && UnknownTasks == 0 && PlanErrors.Count == 0;

    #endregion
}
