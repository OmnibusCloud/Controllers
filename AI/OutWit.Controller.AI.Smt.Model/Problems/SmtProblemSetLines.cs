namespace OutWit.Controller.AI.Smt.Model.Problems;

/// <summary>
/// Reads a problem set a line at a time from a stream, so a set is as large as its stream and
/// not as large as memory: one line is held, never the set. A line ends at a line feed, with or
/// without a carriage return before it; the last line needs no line end. A blank line is passed
/// over (it still counts in the numbering), a byte order mark before the first line is not part
/// of it, and a line longer than a line may be is passed over without being kept and reported
/// as too long.
/// <para>
/// The set has a bound in bytes, every byte counted as it arrives: the mark, the line ends and
/// the blank lines among them. The line that would take the set past it is not read, nor
/// anything after it: the reader stops there and says so once (<see cref="PastBound"/>). Lines
/// are numbered in a number that cannot wrap, however many blank lines a set holds.
/// </para>
/// </summary>
internal sealed class SmtProblemSetLines
{
    #region Constants

    private const int READ_BUFFER_BYTES = 128 * 1024;
    private const int FIRST_LINE_BYTES = 16 * 1024;
    private const byte LINE_FEED = (byte)'\n';
    private const byte CARRIAGE_RETURN = (byte)'\r';

    private static readonly byte[] BYTE_ORDER_MARK = [0xEF, 0xBB, 0xBF];

    #endregion

    #region Fields

    private readonly Stream m_stream;
    private readonly int m_maxLineBytes;
    private readonly long m_maxSetBytes;
    private readonly byte[] m_buffer = new byte[READ_BUFFER_BYTES];
    private byte[] m_line = new byte[FIRST_LINE_BYTES];
    private int m_buffered;
    private int m_position;
    private int m_lineLength;
    private bool m_started;
    private bool m_ended;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a reader of a problem set's lines.
    /// </summary>
    /// <param name="stream">The set, at its start; the caller owns it.</param>
    /// <param name="maxLineBytes">The most bytes a line may have, its line end not counted.</param>
    /// <param name="maxSetBytes">The most bytes the set may have, every byte counted.</param>
    public SmtProblemSetLines(Stream stream, int maxLineBytes, long maxSetBytes)
    {
        m_stream = stream;
        m_maxLineBytes = maxLineBytes;
        m_maxSetBytes = maxSetBytes;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Moves to the next line that is not blank, or to the line at which the set passes its bound.
    /// </summary>
    /// <returns>False when the set has no more lines.</returns>
    public bool MoveNext()
    {
        while (!m_ended)
        {
            ReadLine();
            if (PastBound || TooLong || !IsBlank())
                return true;
        }

        return false;
    }

    private void ReadLine()
    {
        m_lineLength = 0;
        TooLong = false;
        Number++;

        while (true)
        {
            if (m_position == m_buffered && !Fill())
            {
                m_ended = true;
                break;
            }

            var chunk = m_buffer.AsSpan(m_position, m_buffered - m_position);
            var end = chunk.IndexOf(LINE_FEED);
            var taken = end < 0 ? chunk.Length : end;
            var consumed = end < 0 ? taken : taken + 1;

            // The set passes its bound inside this line: nothing more of it is read, and nothing
            // of this line is a row.
            if (consumed > m_maxSetBytes - BytesRead)
            {
                PastBound = true;
                m_ended = true;
                m_lineLength = 0;
                TooLong = false;
                return;
            }

            Keep(chunk[..taken]);
            m_position += consumed;
            BytesRead += consumed;

            if (end >= 0)
                break;
        }

        // The carriage return of a line end is no part of the line, and does not count towards
        // its length: a file is held to the same limit whichever way it ends its lines.
        if (!TooLong && m_lineLength > 0 && m_line[m_lineLength - 1] == CARRIAGE_RETURN)
            m_lineLength--;

        if (m_lineLength > m_maxLineBytes)
        {
            TooLong = true;
            m_lineLength = 0;
        }
    }

    private bool Fill()
    {
        if (!m_started)
        {
            m_started = true;
            return FillStart();
        }

        m_buffered = m_stream.Read(m_buffer, 0, m_buffer.Length);
        m_position = 0;
        return m_buffered > 0;
    }

    /// <summary>
    /// The first bytes of the stream, without the mark an editor may have put before the first
    /// line. A stream may give its bytes one at a time: the start is read until the mark is
    /// there, or cannot be.
    /// </summary>
    private bool FillStart()
    {
        m_buffered = 0;
        m_position = 0;

        while (m_buffered < BYTE_ORDER_MARK.Length)
        {
            var more = m_stream.Read(m_buffer, m_buffered, m_buffer.Length - m_buffered);
            if (more == 0)
                break;

            m_buffered += more;
        }

        if (!m_buffer.AsSpan(0, m_buffered).StartsWith(BYTE_ORDER_MARK))
            return m_buffered > 0;

        BytesRead += BYTE_ORDER_MARK.Length;
        m_position = BYTE_ORDER_MARK.Length;
        return m_position < m_buffered || Fill();
    }

    private void Keep(ReadOnlySpan<byte> bytes)
    {
        if (TooLong)
            return;

        // One byte more than a line may have is kept: it may be the carriage return of the line end.
        if (m_lineLength + (long)bytes.Length > m_maxLineBytes + 1L)
        {
            // Nothing of such a line is kept: it is passed over to its end.
            TooLong = true;
            m_lineLength = 0;
            return;
        }

        if (m_lineLength + bytes.Length > m_line.Length)
            Array.Resize(ref m_line, (int)Math.Min(m_maxLineBytes + 1L, Math.Max(m_line.Length * 2L, m_lineLength + (long)bytes.Length)));

        bytes.CopyTo(m_line.AsSpan(m_lineLength));
        m_lineLength += bytes.Length;
    }

    private bool IsBlank()
    {
        foreach (var value in m_line.AsSpan(0, m_lineLength))
        {
            if (value is not ((byte)' ' or (byte)'\t' or CARRIAGE_RETURN))
                return false;
        }

        return true;
    }

    #endregion

    #region Properties

    /// <summary>The number of the current line, from 1, blank lines counted.</summary>
    public long Number { get; private set; }

    /// <summary>Whether the current line is longer than a line may be; its bytes were not kept.</summary>
    public bool TooLong { get; private set; }

    /// <summary>
    /// Whether the set passed its bound in bytes inside the current line: the line was not read,
    /// and the set has no more lines.
    /// </summary>
    public bool PastBound { get; private set; }

    /// <summary>The current line's bytes without its line end, valid until the next move.</summary>
    public ReadOnlySpan<byte> Line => m_line.AsSpan(0, m_lineLength);

    /// <summary>The bytes of the stream passed so far: every line up to the current one with its line end, and the byte order mark. Never more than the set's bound.</summary>
    public long BytesRead { get; private set; }

    #endregion
}
