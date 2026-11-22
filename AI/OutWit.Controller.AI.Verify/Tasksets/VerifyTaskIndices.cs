namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// The task indices a taskset has used so far, kept to tell a duplicate. The indices are kept
/// in pages made as indices fall into them (<see cref="VerifyTaskIndexPage"/>): a page with many
/// is a bitmap, so a taskset numbered from zero costs an eighth of a byte a task, and a page
/// with few is a short list, so a taskset numbered far apart costs a few bytes a task and never
/// a page of bits for each. Nothing of a task's text is kept. The line of an index is
/// remembered for the first <see cref="MAX_REMEMBERED_LINES"/> tasks only: a message names the
/// earlier line when it is known and says "an earlier line" otherwise.
/// </summary>
public sealed class VerifyTaskIndices
{
    #region Constants

    /// <summary>How many tasks' lines are remembered for the message of a duplicate.</summary>
    public const int MAX_REMEMBERED_LINES = 65536;

    private const int PAGE_SHIFT = 16;
    private const int PAGE_MASK = VerifyTaskIndexPage.SIZE - 1;

    #endregion

    #region Fields

    private readonly Dictionary<int, VerifyTaskIndexPage> m_pages = new();
    private readonly Dictionary<int, int> m_lines = new();

    #endregion

    #region Functions

    /// <summary>Adds an index.</summary>
    /// <param name="index">The index, 0 or more.</param>
    /// <param name="line">The line that uses it.</param>
    /// <param name="earlier">The line that used it before, or 0 when it is new or that line is not remembered.</param>
    /// <returns>False when the index was used before.</returns>
    public bool Add(int index, int line, out int earlier)
    {
        earlier = 0;

        if (!m_pages.TryGetValue(index >> PAGE_SHIFT, out var page))
            m_pages[index >> PAGE_SHIFT] = page = new VerifyTaskIndexPage();

        if (!page.Add(index & PAGE_MASK))
        {
            m_lines.TryGetValue(index, out earlier);
            return false;
        }

        if (m_lines.Count < MAX_REMEMBERED_LINES)
            m_lines[index] = line;

        Count++;
        return true;
    }

    #endregion

    #region Properties

    /// <summary>How many indices were added.</summary>
    public int Count { get; private set; }

    #endregion
}
