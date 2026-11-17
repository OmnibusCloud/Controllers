namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// The messages of one kind that a look at a taskset gives (malformed lines, clamped limits,
/// tasks that cannot be planned), bounded in number and in length: a taskset of a million bad
/// lines says so in a few lines and a count, since the messages travel in a job variable.
/// </summary>
public sealed class VerifyMessageList
{
    #region Constants

    private const string CUT_MARK = "...";

    #endregion

    #region Fields

    private readonly List<string> m_shown = [];
    private readonly int m_max;

    #endregion

    #region Constructors

    /// <summary>A list that shows at most <see cref="VerifyTasksetLimits.MAX_MESSAGES"/> messages.</summary>
    public VerifyMessageList()
        : this(VerifyTasksetLimits.MAX_MESSAGES)
    {
    }

    /// <summary>A list that shows at most a number of messages.</summary>
    /// <param name="max">The most messages kept.</param>
    public VerifyMessageList(int max)
    {
        m_max = Math.Max(0, max);
    }

    #endregion

    #region Functions

    /// <summary>Adds a message: kept, cut to the length a message may have, while the list has room; counted either way.</summary>
    /// <param name="message">The message.</param>
    public void Add(string message)
    {
        Count++;
        if (m_shown.Count < m_max)
            m_shown.Add(Bounded(message));
    }

    /// <summary>Adds several messages.</summary>
    /// <param name="messages">The messages.</param>
    public void AddRange(IEnumerable<string> messages)
    {
        foreach (var message in messages)
            Add(message);
    }

    /// <summary>The messages as they travel: those kept, and one line for the rest when there are more.</summary>
    /// <param name="what">What the messages are, for that line (for example "malformed line(s)").</param>
    /// <returns>The lines.</returns>
    public List<string> ToList(string what)
    {
        var lines = new List<string>(m_shown);
        if (Count > m_shown.Count)
            lines.Add($"and {Count - m_shown.Count} more {what} not shown");

        return lines;
    }

    /// <summary>A message cut to the length one may have.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The message, or its start and a mark.</returns>
    public static string Bounded(string message)
    {
        return message.Length <= VerifyTasksetLimits.MAX_MESSAGE_LENGTH
            ? message
            : string.Concat(message.AsSpan(0, VerifyTasksetLimits.MAX_MESSAGE_LENGTH - CUT_MARK.Length), CUT_MARK);
    }

    #endregion

    #region Properties

    /// <summary>How many messages were added, kept or not.</summary>
    public int Count { get; private set; }

    /// <summary>The messages kept.</summary>
    public IReadOnlyList<string> Shown => m_shown;

    #endregion
}
