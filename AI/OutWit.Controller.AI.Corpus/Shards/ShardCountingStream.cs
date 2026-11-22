namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// A write-only pass-through that counts every byte written to the stream under it: the bytes a
/// shard's compressed text has taken so far, held to <see cref="ShardProductLimits.MaxTextBytes"/>
/// while the text is written.
/// </summary>
public sealed class ShardCountingStream : Stream
{
    #region Fields

    private readonly Stream m_inner;

    #endregion

    #region Constructors

    /// <summary>Wraps a stream; the caller keeps owning it.</summary>
    /// <param name="inner">The stream the bytes go to.</param>
    public ShardCountingStream(Stream inner)
    {
        m_inner = inner;
    }

    #endregion

    #region Stream

    /// <summary>Writes the bytes through and counts them.</summary>
    /// <param name="buffer">The bytes.</param>
    /// <param name="offset">Where they start.</param>
    /// <param name="count">How many.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    /// <summary>Writes the bytes through and counts them.</summary>
    /// <param name="buffer">The bytes.</param>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        m_inner.Write(buffer);
        Written += buffer.Length;
    }

    /// <summary>Flushes the stream under it.</summary>
    public override void Flush()
    {
        m_inner.Flush();
    }

    /// <summary>Not supported: the stream is write-only.</summary>
    /// <param name="buffer">Unused.</param>
    /// <param name="offset">Unused.</param>
    /// <param name="count">Unused.</param>
    /// <returns>Never.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not supported: the stream is write-only.</summary>
    /// <param name="offset">Unused.</param>
    /// <param name="origin">Unused.</param>
    /// <returns>Never.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not supported: the stream is write-only.</summary>
    /// <param name="value">Unused.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    #endregion

    #region Properties

    /// <summary>The bytes written so far.</summary>
    public long Written { get; private set; }

    /// <summary>False: the stream is write-only.</summary>
    public override bool CanRead => false;

    /// <summary>False: the stream is write-only.</summary>
    public override bool CanSeek => false;

    /// <summary>True.</summary>
    public override bool CanWrite => true;

    /// <summary>Not supported.</summary>
    public override long Length => throw new NotSupportedException();

    /// <summary>Not supported.</summary>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    #endregion
}
