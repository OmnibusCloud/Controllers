using System.Security.Cryptography;

namespace OutWit.Controller.AI.Proof.Spike.Precheck;

/// <summary>
/// Counts (and optionally hashes) the bytes read through it, and refuses a stream past its ceiling
/// while it is read, before anything larger is written or kept.
/// </summary>
public sealed class CountingStream : Stream
{
    #region Fields

    private readonly Stream m_inner;

    private readonly long m_maxBytes;

    private readonly IncrementalHash? m_hash;

    #endregion

    #region Constructors

    /// <summary>
    /// Wraps a stream.
    /// </summary>
    /// <param name="inner">The stream read.</param>
    /// <param name="maxBytes">The ceiling on the bytes read.</param>
    /// <param name="sha256">Whether the bytes are hashed.</param>
    public CountingStream(Stream inner, long maxBytes, bool sha256)
    {
        m_inner = inner;
        m_maxBytes = maxBytes;
        m_hash = sha256 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The SHA-256 of the bytes read so far, in lower-case hex (null when not hashed).
    /// </summary>
    /// <returns>The digest.</returns>
    public string? Digest()
    {
        return m_hash is null ? null : Convert.ToHexStringLower(m_hash.GetCurrentHash());
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = m_inner.Read(buffer, offset, count);
        if (read <= 0)
            return read;
        BytesRead += read;
        if (BytesRead > m_maxBytes)
            throw new PrecheckRefusalException("too_large", $"more than {m_maxBytes} bytes");
        m_hash?.AppendData(buffer, offset, read);
        return read;
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
        {
            m_hash?.Dispose();
            m_inner.Dispose();
        }
        base.Dispose(disposing);
    }

    #endregion

    #region Properties

    /// <summary>
    /// The bytes read so far.
    /// </summary>
    public long BytesRead { get; private set; }

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
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    #endregion
}
