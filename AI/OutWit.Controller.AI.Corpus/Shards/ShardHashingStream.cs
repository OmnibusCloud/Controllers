using System.Security.Cryptography;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// A write-only pass-through that hashes every byte written to the stream under it: the
/// signatures blob's identity is computed while the blob is written, with no second read.
/// </summary>
public sealed class ShardHashingStream : Stream
{
    #region Fields

    private readonly Stream m_inner;
    private readonly IncrementalHash m_hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    #endregion

    #region Constructors

    /// <summary>Wraps a stream; the caller keeps owning it.</summary>
    /// <param name="inner">The stream the bytes go to.</param>
    public ShardHashingStream(Stream inner)
    {
        m_inner = inner;
    }

    #endregion

    #region Functions

    /// <summary>The SHA-256 of every byte written so far.</summary>
    /// <returns>Lower-case hex.</returns>
    public string Sha256()
    {
        return Convert.ToHexStringLower(m_hash.GetCurrentHash());
    }

    #endregion

    #region Stream

    /// <summary>Writes the bytes through and hashes them.</summary>
    /// <param name="buffer">The bytes.</param>
    /// <param name="offset">Where they start.</param>
    /// <param name="count">How many.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    /// <summary>Writes the bytes through and hashes them.</summary>
    /// <param name="buffer">The bytes.</param>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        m_hash.AppendData(buffer);
        m_inner.Write(buffer);
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

    /// <summary>Releases the hash; the stream under it is the caller's.</summary>
    /// <param name="disposing">Whether managed resources are released.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            m_hash.Dispose();

        base.Dispose(disposing);
    }

    #endregion

    #region Properties

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
