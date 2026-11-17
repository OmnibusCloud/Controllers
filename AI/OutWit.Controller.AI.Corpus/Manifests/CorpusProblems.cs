namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// The problems (or the notes) found in a manifest, as a bounded list: the first
/// <see cref="MAX_LISTED"/> are kept by line, every one is counted, and the list ends with how
/// many more there are. A manifest is a lab's input and may be wrong in every one of a million
/// lines; what the server answers with — a preflight's messages, a refusal's text — does not grow
/// with it.
/// </summary>
public sealed class CorpusProblems
{
    #region Constants

    /// <summary>The most problems listed by line.</summary>
    public const int MAX_LISTED = 100;

    /// <summary>The most characters of one line: a line quotes the manifest (a URL, a key), and the manifest's lines have no length of their own.</summary>
    public const int MAX_LINE_CHARS = 512;

    #endregion

    #region Fields

    private readonly List<string> m_listed = [];
    private readonly int m_max;

    #endregion

    #region Constructors

    /// <summary>Creates an empty list.</summary>
    /// <param name="max">The most entries kept by line.</param>
    public CorpusProblems(int max = MAX_LISTED)
    {
        m_max = Math.Max(1, max);
    }

    #endregion

    #region Functions

    /// <summary>Adds a problem: kept when the list has room, counted either way.</summary>
    /// <param name="message">The problem, naming where it was found.</param>
    public void Add(string message)
    {
        Count++;
        if (m_listed.Count < m_max)
            m_listed.Add(Bounded(message));
    }

    /// <summary>Adds problems found elsewhere, of which only some were kept by line.</summary>
    /// <param name="listed">The ones kept.</param>
    /// <param name="count">How many there were in all.</param>
    public void Add(IReadOnlyList<string> listed, int count)
    {
        foreach (var message in listed)
        {
            if (m_listed.Count < m_max)
                m_listed.Add(Bounded(message));
        }

        Count += Math.Max(count, listed.Count);
    }

    /// <summary>The list as it is handed on: the entries kept and, when there are more, one line saying how many.</summary>
    /// <param name="what">What the entries are, for that line: "problem" or "note".</param>
    /// <returns>At most <see cref="MAX_LISTED"/> entries and one more line.</returns>
    public List<string> ToList(string what = "problem")
    {
        var lines = new List<string>(m_listed);
        if (Count > m_listed.Count)
            lines.Add($"{Count - m_listed.Count} more {what}(s) of the manifest are not listed here.");

        return lines;
    }

    private static string Bounded(string message)
    {
        return message.Length <= MAX_LINE_CHARS ? message : message[..MAX_LINE_CHARS] + "...";
    }

    #endregion

    #region Properties

    /// <summary>Every problem added, listed or not.</summary>
    public int Count { get; private set; }

    /// <summary>The problems kept by line.</summary>
    public IReadOnlyList<string> Listed => m_listed;

    #endregion
}
