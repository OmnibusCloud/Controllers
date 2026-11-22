namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A read-only stream that hands its bytes over a few at a time, as a slow link or a small buffer
/// does: what a reader gives must not depend on where the reads were cut.
/// </summary>
/// <param name="bytes">The content.</param>
/// <param name="chunk">The most bytes one read returns.</param>
/// <param name="seekable">Whether the stream can be positioned (a file can; a link cannot).</param>
public sealed class TrickleStream(byte[] bytes, int chunk, bool seekable = false) : Stream
{
    #region Fields

    private int m_position;

    #endregion

    #region Stream

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        var take = Math.Min(Math.Min(count, chunk), bytes.Length - m_position);
        Array.Copy(bytes, m_position, buffer, offset, take);
        m_position += take;
        return take;
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!seekable)
            throw new NotSupportedException();

        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => m_position + offset,
            _ => bytes.Length + offset
        };

        if (target < 0 || target > bytes.Length)
            throw new IOException("A position outside the stream.");

        m_position = (int)target;
        return m_position;
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
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => seekable;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => bytes.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => m_position;
        set => Seek(value, SeekOrigin.Begin);
    }

    #endregion
}
