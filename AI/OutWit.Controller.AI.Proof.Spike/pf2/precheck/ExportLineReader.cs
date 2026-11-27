namespace OutWit.Controller.AI.Proof.Spike.Precheck;

/// <summary>
/// Splits a stream into lines without holding more than the longest allowed line in memory.
/// </summary>
public sealed class ExportLineReader
{
    #region Constants

    private const int INITIAL_BUFFER = 4 << 20;

    #endregion

    #region Fields

    private readonly Stream m_stream;

    private readonly int m_maxLine;

    private byte[] m_buffer = new byte[INITIAL_BUFFER];

    private int m_start;

    private int m_end;

    private int m_scan;

    private bool m_eof;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a reader over a stream.
    /// </summary>
    /// <param name="stream">The export's bytes (already decompressed).</param>
    /// <param name="maxLine">The longest line allowed, without its newline.</param>
    public ExportLineReader(Stream stream, int maxLine)
    {
        m_stream = stream;
        m_maxLine = maxLine;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the next line (without its newline).
    /// </summary>
    /// <param name="line">The line; valid until the next call.</param>
    /// <returns>False at the end of the stream.</returns>
    /// <exception cref="PrecheckRefusalException"><c>line_too_long</c> when a line passes the limit.</exception>
    public bool TryReadLine(out ReadOnlySpan<byte> line)
    {
        while (true)
        {
            var found = m_buffer.AsSpan(m_scan, m_end - m_scan).IndexOf((byte)'\n');
            if (found >= 0)
            {
                var stop = m_scan + found;
                if (stop - m_start > m_maxLine)
                    throw TooLong(stop - m_start);
                line = m_buffer.AsSpan(m_start, stop - m_start);
                LongestLine = Math.Max(LongestLine, stop - m_start);
                m_start = stop + 1;
                m_scan = m_start;
                return true;
            }
            if (m_end - m_start > m_maxLine)
                throw TooLong(m_end - m_start);
            if (m_eof)
            {
                if (m_end == m_start)
                {
                    line = default;
                    return false;
                }
                line = m_buffer.AsSpan(m_start, m_end - m_start);
                LongestLine = Math.Max(LongestLine, m_end - m_start);
                MissingFinalNewline = true;
                m_start = m_end;
                m_scan = m_end;
                return true;
            }
            Fill();
        }
    }

    private void Fill()
    {
        m_scan = m_end;
        if (m_start > 0)
        {
            var length = m_end - m_start;
            Buffer.BlockCopy(m_buffer, m_start, m_buffer, 0, length);
            m_scan -= m_start;
            m_end = length;
            m_start = 0;
        }
        if (m_end == m_buffer.Length)
        {
            var bigger = new byte[Math.Min((long)m_buffer.Length * 2, (long)m_maxLine + INITIAL_BUFFER)];
            Buffer.BlockCopy(m_buffer, 0, bigger, 0, m_end);
            m_buffer = bigger;
        }
        var read = m_stream.Read(m_buffer, m_end, m_buffer.Length - m_end);
        if (read == 0)
            m_eof = true;
        m_end += read;
    }

    private PrecheckRefusalException TooLong(long length)
    {
        return new PrecheckRefusalException("line_too_long", $"a line of more than {length} bytes (limit {m_maxLine})");
    }

    #endregion

    #region Properties

    /// <summary>
    /// The longest line read so far, in bytes.
    /// </summary>
    public int LongestLine { get; private set; }

    /// <summary>
    /// Whether the last line had no newline.
    /// </summary>
    public bool MissingFinalNewline { get; private set; }

    /// <summary>
    /// The size of the line buffer (the reader's own memory).
    /// </summary>
    public int BufferBytes => m_buffer.Length;

    #endregion
}
