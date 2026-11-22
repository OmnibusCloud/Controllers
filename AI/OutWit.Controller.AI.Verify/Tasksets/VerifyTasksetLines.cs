namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>
/// Reads a taskset a line at a time from a stream, so a taskset is as large as its blob and
/// not as large as memory: one line is held, never the file. A blank line is skipped (it still
/// counts in the numbering), a byte-order mark before the first line is not part of it, and a
/// line longer than <see cref="VerifyTasksetLimits.MAX_LINE_BYTES"/> is passed over without
/// being kept and reported as too long.
/// </summary>
public sealed class VerifyTasksetLines
{
    #region Constants

    private const int READ_BUFFER_BYTES = 128 * 1024;
    private const int FIRST_LINE_BYTES = 16 * 1024;
    private const byte LINE_END = (byte)'\n';

    private static readonly byte[] BYTE_ORDER_MARK = [0xEF, 0xBB, 0xBF];

    #endregion

    #region Fields

    private readonly Stream m_stream;
    private readonly int m_maxLineBytes;
    private readonly byte[] m_buffer = new byte[READ_BUFFER_BYTES];
    private byte[] m_line = new byte[FIRST_LINE_BYTES];
    private int m_buffered;
    private int m_position;
    private int m_lineLength;
    private bool m_started;
    private bool m_ended;

    #endregion

    #region Constructors

    /// <summary>A reader of a taskset's lines.</summary>
    /// <param name="stream">The taskset, at its start; the caller owns it.</param>
    public VerifyTasksetLines(Stream stream)
        : this(stream, VerifyTasksetLimits.MAX_LINE_BYTES)
    {
    }

    /// <summary>A reader with a line bound of its own: for tests of the bound.</summary>
    /// <param name="stream">The taskset, at its start; the caller owns it.</param>
    /// <param name="maxLineBytes">The most bytes a line may have.</param>
    public VerifyTasksetLines(Stream stream, int maxLineBytes)
    {
        m_stream = stream;
        m_maxLineBytes = maxLineBytes;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Moves to the next line that is not blank.
    /// </summary>
    /// <returns>False when the taskset has no more lines.</returns>
    public bool MoveNext()
    {
        while (!m_ended)
        {
            ReadLine();
            if (TooLong || !IsBlank())
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
                return;
            }

            var chunk = m_buffer.AsSpan(m_position, m_buffered - m_position);
            var end = chunk.IndexOf(LINE_END);
            var taken = end < 0 ? chunk.Length : end;

            Keep(chunk[..taken]);
            m_position += end < 0 ? taken : taken + 1;

            if (end >= 0)
                return;
        }
    }

    private bool Fill()
    {
        m_buffered = m_stream.Read(m_buffer, 0, m_buffer.Length);
        m_position = 0;

        if (!m_started)
        {
            m_started = true;

            // A mark an editor put before the first line. A read may give fewer bytes than the
            // mark has only at the end of a stream that short, which then holds no task anyway.
            if (m_buffer.AsSpan(0, m_buffered).StartsWith(BYTE_ORDER_MARK))
                m_position = BYTE_ORDER_MARK.Length;
        }

        return m_buffered > 0;
    }

    private void Keep(ReadOnlySpan<byte> bytes)
    {
        if (TooLong)
            return;

        if (m_lineLength + (long)bytes.Length > m_maxLineBytes)
        {
            // Nothing of such a line is kept: it is passed over to its end.
            TooLong = true;
            m_lineLength = 0;
            return;
        }

        if (m_lineLength + bytes.Length > m_line.Length)
            Array.Resize(ref m_line, (int)Math.Min(m_maxLineBytes, Math.Max(m_line.Length * 2L, m_lineLength + (long)bytes.Length)));

        bytes.CopyTo(m_line.AsSpan(m_lineLength));
        m_lineLength += bytes.Length;
    }

    private bool IsBlank()
    {
        foreach (var value in m_line.AsSpan(0, m_lineLength))
        {
            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r'))
                return false;
        }

        return true;
    }

    #endregion

    #region Properties

    /// <summary>The number of the current line, from 1, blank lines counted.</summary>
    public int Number { get; private set; }

    /// <summary>Whether the current line is longer than a line may be; its bytes were not kept.</summary>
    public bool TooLong { get; private set; }

    /// <summary>The current line's bytes, valid until the next move.</summary>
    public ReadOnlyMemory<byte> Line => m_line.AsMemory(0, m_lineLength);

    #endregion
}
