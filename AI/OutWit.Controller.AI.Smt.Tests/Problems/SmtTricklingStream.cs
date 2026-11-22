namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// A stream that gives its bytes one a read, as a slow connection may: what a reader does at the
/// start of a stream and across its buffers must not depend on how much a read returns.
/// </summary>
internal sealed class SmtTricklingStream : Stream
{
    #region Fields

    private readonly byte[] m_bytes;
    private int m_position;

    #endregion

    #region Constructors

    /// <summary>Creates the stream over its bytes.</summary>
    /// <param name="bytes">What the stream holds.</param>
    public SmtTricklingStream(byte[] bytes)
    {
        m_bytes = bytes;
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (m_position >= m_bytes.Length || count == 0)
            return 0;

        buffer[offset] = m_bytes[m_position++];
        return 1;
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
