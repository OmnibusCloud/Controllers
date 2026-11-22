using System.Text;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// A taskset that is never in memory: a read-only stream whose lines are made as they are read,
/// so a test of a hundred thousand tasks holds one line of them. It also measures the managed
/// heap as it is read (what is live after a full collection, every few thousand lines), which
/// is how a test sees what its reader kept.
/// </summary>
internal sealed class VerifySyntheticTaskset : Stream
{
    #region Constants

    private const int SAMPLE_EVERY_LINES = 4096;

    #endregion

    #region Fields

    private readonly int m_lines;
    private readonly Func<int, string> m_line;
    private byte[] m_current = [];
    private int m_offset;
    private int m_next;

    #endregion

    #region Constructors

    /// <summary>A taskset of a number of lines.</summary>
    /// <param name="lines">How many lines.</param>
    /// <param name="line">Makes a line (without its end) from its number, from 0.</param>
    public VerifySyntheticTaskset(int lines, Func<int, string> line)
    {
        m_lines = lines;
        m_line = line;
    }

    #endregion

    #region Stream

    public override int Read(byte[] buffer, int offset, int count)
    {
        var written = 0;
        while (written < count)
        {
            if (m_offset == m_current.Length)
            {
                if (m_next == m_lines)
                    break;

                if (m_next % SAMPLE_EVERY_LINES == SAMPLE_EVERY_LINES - 1)
                    PeakHeapBytes = Math.Max(PeakHeapBytes, GC.GetTotalMemory(forceFullCollection: true));

                m_current = Encoding.UTF8.GetBytes(m_line(m_next++) + "\n");
                m_offset = 0;
                Bytes += m_current.Length;
            }

            var take = Math.Min(count - written, m_current.Length - m_offset);
            Buffer.BlockCopy(m_current, m_offset, buffer, offset + written, take);
            m_offset += take;
            written += take;
        }

        return written;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    #endregion

    #region Properties

    /// <summary>The bytes the stream has given so far.</summary>
    public long Bytes { get; private set; }

    /// <summary>The most that was live on the managed heap at the moments the stream measured it.</summary>
    public long PeakHeapBytes { get; private set; }

    #endregion
}
