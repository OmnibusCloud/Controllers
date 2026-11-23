using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// The transcript of one session, of which only the end is kept: every command the controller
/// sent, each on a line behind <c>&gt; </c>, and what the solver answered, as it wrote it. The
/// problem's own text is not in it (one line stands for it): the problem is known by its hash,
/// and the transcript is about what was asked of it and what came back.
/// <para>
/// A session's answers can be megabytes (a model); the transcript holds no more than twice its
/// limit at any time, and gives its last <see cref="LIMIT_BYTES"/> bytes of UTF-8 in the end.
/// </para>
/// </summary>
internal sealed class SmtTranscript
{
    #region Constants

    /// <summary>The most bytes of UTF-8 a session's transcript keeps: its end.</summary>
    public const int LIMIT_BYTES = 8 * 1024;

    private const string SENT_PREFIX = "> ";
    private const string NOTE_PREFIX = "; ";
    private const char LINE_FEED = '\n';

    private const int SURROGATE_PAIR_BYTES = 4;

    #endregion

    #region Fields

    private readonly StringBuilder m_text = new();

    #endregion

    #region Functions

    /// <summary>
    /// Notes a command the controller sent.
    /// </summary>
    /// <param name="command">The command.</param>
    public void Sent(SmtExpression command)
    {
        Append(SENT_PREFIX);
        Append(command.ToString());
        Append(LINE_FEED);
    }

    /// <summary>
    /// Notes something that is neither a command nor an answer, as a comment line: what stands
    /// for the problem's text, what the solver wrote to its other stream.
    /// </summary>
    /// <param name="note">The note, one line.</param>
    public void Note(string note)
    {
        Append(NOTE_PREFIX);
        Append(note);
        Append(LINE_FEED);
    }

    /// <summary>
    /// Notes what the solver wrote, as it wrote it.
    /// </summary>
    /// <param name="answer">The text.</param>
    public void Answer(string answer)
    {
        Append(answer);
        if (answer.Length > 0 && answer[^1] != LINE_FEED)
            Append(LINE_FEED);
    }

    /// <summary>
    /// The end of the transcript: its last <see cref="LIMIT_BYTES"/> bytes of UTF-8, from the
    /// first whole character on.
    /// </summary>
    /// <returns>The text.</returns>
    public string Tail()
    {
        var start = m_text.Length;
        var bytes = 0;
        while (start > 0)
        {
            var character = m_text[start - 1];
            var pair = char.IsLowSurrogate(character) && start > 1 && char.IsHighSurrogate(m_text[start - 2]);
            var size = pair ? SURROGATE_PAIR_BYTES : Encoding.UTF8.GetByteCount([character]);
            if (bytes + size > LIMIT_BYTES)
                break;

            bytes += size;
            start -= pair ? 2 : 1;
        }

        return m_text.ToString(start, m_text.Length - start);
    }

    #endregion

    #region Tools

    private void Append(string text)
    {
        // A character is at least one byte: the last LIMIT_BYTES characters hold the last LIMIT_BYTES bytes.
        if (text.Length >= LIMIT_BYTES)
        {
            m_text.Clear();
            m_text.Append(text, text.Length - LIMIT_BYTES, LIMIT_BYTES);
            return;
        }

        m_text.Append(text);
        Trim();
    }

    private void Append(char character)
    {
        m_text.Append(character);
        Trim();
    }

    private void Trim()
    {
        if (m_text.Length > 2 * LIMIT_BYTES)
            m_text.Remove(0, m_text.Length - LIMIT_BYTES);
    }

    #endregion
}
