namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// A stream whose bytes are made while it is read, a piece at a time: a problem set far larger
/// than a test should hold in memory. It counts what was read of it.
/// </summary>
internal sealed class SmtGeneratedStream : Stream
{
    #region Fields

    private readonly IEnumerator<byte[]> m_pieces;
    private byte[] m_piece = [];
    private int m_offset;

    #endregion

    #region Constructors

    /// <summary>Creates the stream of the pieces a sequence gives, in order.</summary>
    /// <param name="pieces">The pieces; each is asked for when the one before it has been read.</param>
    public SmtGeneratedStream(IEnumerable<byte[]> pieces)
    {
        m_pieces = pieces.GetEnumerator();
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        while (m_offset == m_piece.Length)
        {
            if (!m_pieces.MoveNext())
                return 0;

            m_piece = m_pieces.Current;
            m_offset = 0;
        }

        var taken = Math.Min(count, m_piece.Length - m_offset);
        Array.Copy(m_piece, m_offset, buffer, offset, taken);
        m_offset += taken;
        BytesGiven += taken;
        return taken;
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            m_pieces.Dispose();

        base.Dispose(disposing);
    }

    #endregion

    #region Properties

    /// <summary>The bytes read of the stream so far.</summary>
    public long BytesGiven { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => BytesGiven;
        set => throw new NotSupportedException();
    }

    #endregion
}
