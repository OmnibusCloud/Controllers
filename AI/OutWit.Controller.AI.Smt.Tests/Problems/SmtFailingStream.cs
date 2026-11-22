namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// A stream that fails with an <see cref="IOException"/> once so many of its bytes were read: a
/// file that is taken away, a connection that drops.
/// </summary>
internal sealed class SmtFailingStream : Stream
{
    #region Fields

    private readonly byte[] m_bytes;
    private readonly int m_failAt;
    private int m_position;

    #endregion

    #region Constructors

    /// <summary>Creates the stream over its bytes.</summary>
    /// <param name="bytes">What the stream holds.</param>
    /// <param name="failAt">How many bytes are given before a read fails.</param>
    public SmtFailingStream(byte[] bytes, int failAt)
    {
        m_bytes = bytes;
        m_failAt = failAt;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (m_position >= m_failAt)
            throw new IOException("the stream is gone");

        var taken = Math.Min(count, Math.Min(m_failAt, m_bytes.Length) - m_position);
        Array.Copy(m_bytes, m_position, buffer, offset, taken);
        m_position += taken;
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

    #endregion

    #region Properties

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => m_bytes.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => m_position;
        set => throw new NotSupportedException();
    }

    #endregion
}
