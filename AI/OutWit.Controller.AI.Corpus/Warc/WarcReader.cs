using System.IO.Compression;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Warc;

/// <summary>
/// Streams the records of a WARC or WET file, gzip-compressed (Common Crawl's layout: one gzip
/// member per record, concatenated) or plain. One record is in memory at a time, never the
/// shard; a record larger than the cap is skipped and counted, not loaded. Malformed input is
/// an error naming the byte offset, except a truncated tail, which ends the file with the
/// records that were complete — the honest reading of a partial download.
/// </summary>
public sealed class WarcReader : IDisposable
{
    #region Constants

    /// <summary>Payload cap per record: the largest a single document is allowed to be.</summary>
    public const int DEFAULT_MAX_RECORD_BYTES = 64 * 1024 * 1024;

    private const string VERSION_PREFIX = "WARC/";
    private const string CONTENT_LENGTH = "Content-Length";
    private const int MAX_HEADER_BYTES = 64 * 1024;
    private static readonly byte[] GZIP_MAGIC = [0x1F, 0x8B];

    #endregion

    #region Fields

    private readonly Stream m_stream;
    private readonly bool m_ownsStream;
    private readonly int m_maxRecordBytes;
    private long m_offset;
    private int m_skippedOversized;
    private bool m_truncated;

    #endregion

    #region Constructors

    /// <summary>
    /// Opens a reader over a stream, decompressing when the stream starts with the gzip magic.
    /// </summary>
    /// <param name="stream">The raw file stream (seekable or not).</param>
    /// <param name="maxRecordBytes">The payload cap per record.</param>
    /// <param name="ownsStream">Whether disposing the reader disposes the stream.</param>
    public WarcReader(Stream stream, int maxRecordBytes = DEFAULT_MAX_RECORD_BYTES, bool ownsStream = true)
    {
        m_maxRecordBytes = maxRecordBytes;
        m_ownsStream = ownsStream;

        var buffered = new BufferedStream(stream, 1 << 16);
        m_stream = IsGzip(buffered) ? new GZipStream(buffered, CompressionMode.Decompress) : buffered;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Opens a WARC/WET file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="maxRecordBytes">The payload cap per record.</param>
    /// <returns>The reader.</returns>
    public static WarcReader Open(string path, int maxRecordBytes = DEFAULT_MAX_RECORD_BYTES)
    {
        return new WarcReader(File.OpenRead(path), maxRecordBytes);
    }

    /// <summary>
    /// Reads the records in order. Oversized records are skipped (see <see cref="SkippedOversized"/>);
    /// a truncated tail ends the sequence (see <see cref="Truncated"/>).
    /// </summary>
    /// <returns>The records, one at a time.</returns>
    /// <exception cref="WarcFormatException">The input is not WARC where a record should start, or a header is malformed.</exception>
    public IEnumerable<WarcRecord> ReadRecords()
    {
        while (true)
        {
            var record = ReadRecord();
            if (record == null)
                yield break;

            yield return record;
        }
    }

    private WarcRecord? ReadRecord()
    {
        while (true)
        {
            var recordOffset = m_offset;

            // Records are separated by CRLF CRLF; tolerate any run of blank lines between them.
            string? versionLine;
            do
            {
                versionLine = ReadLine();
                if (versionLine == null)
                    return null;
            } while (versionLine.Length == 0);

            if (!versionLine.StartsWith(VERSION_PREFIX, StringComparison.Ordinal))
                throw new WarcFormatException($"expected a WARC version line at offset {recordOffset}, got '{Clip(versionLine)}'");

            var headers = new List<KeyValuePair<string, string>>();
            long contentLength = -1;
            var headerBytes = 0;
            while (true)
            {
                var line = ReadLine();
                if (line == null)
                {
                    m_truncated = true;
                    return null;
                }

                if (line.Length == 0)
                    break;

                headerBytes += line.Length + 2;
                if (headerBytes > MAX_HEADER_BYTES)
                    throw new WarcFormatException($"header block past {MAX_HEADER_BYTES} bytes at offset {recordOffset}");

                var colon = line.IndexOf(':');
                if (colon <= 0)
                    throw new WarcFormatException($"malformed header line at offset {recordOffset}: '{Clip(line)}'");

                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                headers.Add(new KeyValuePair<string, string>(name, value));

                if (string.Equals(name, CONTENT_LENGTH, StringComparison.OrdinalIgnoreCase))
                {
                    if (!long.TryParse(value, out contentLength) || contentLength < 0)
                        throw new WarcFormatException($"bad Content-Length '{Clip(value)}' at offset {recordOffset}");
                }
            }

            if (contentLength < 0)
                throw new WarcFormatException($"record without Content-Length at offset {recordOffset}");

            if (contentLength > m_maxRecordBytes)
            {
                // Too big to hold: skip the payload and count the record, keep reading.
                if (!Skip(contentLength))
                {
                    m_truncated = true;
                    return null;
                }

                m_skippedOversized++;
                continue;
            }

            var payload = new byte[contentLength];
            if (!ReadExactly(payload))
            {
                m_truncated = true;
                return null;
            }

            return new WarcRecord(versionLine, headers, payload, recordOffset);
        }
    }

    private string? ReadLine()
    {
        var buffer = new List<byte>(128);
        while (true)
        {
            var b = m_stream.ReadByte();
            if (b < 0)
            {
                if (buffer.Count == 0)
                    return null;
                break;
            }

            m_offset++;
            if (b == '\n')
                break;

            buffer.Add((byte)b);
            if (buffer.Count > MAX_HEADER_BYTES)
                throw new WarcFormatException($"header line past {MAX_HEADER_BYTES} bytes at offset {m_offset}");
        }

        if (buffer.Count > 0 && buffer[^1] == '\r')
            buffer.RemoveAt(buffer.Count - 1);

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private bool ReadExactly(byte[] target)
    {
        var read = 0;
        while (read < target.Length)
        {
            var n = m_stream.Read(target, read, target.Length - read);
            if (n <= 0)
                return false;

            read += n;
        }

        m_offset += read;
        return true;
    }

    private bool Skip(long count)
    {
        var scratch = new byte[1 << 16];
        var remaining = count;
        while (remaining > 0)
        {
            var n = m_stream.Read(scratch, 0, (int)Math.Min(scratch.Length, remaining));
            if (n <= 0)
                return false;

            remaining -= n;
            m_offset += n;
        }

        return true;
    }

    private static bool IsGzip(BufferedStream stream)
    {
        // Peek two bytes through a seekable buffer; a non-seekable stream is wrapped first.
        if (!stream.CanSeek)
            throw new ArgumentException("the WARC stream must be seekable for the gzip probe; wrap it in a seekable buffer");

        var position = stream.Position;
        var b0 = stream.ReadByte();
        var b1 = stream.ReadByte();
        stream.Position = position;
        return b0 == GZIP_MAGIC[0] && b1 == GZIP_MAGIC[1];
    }

    private static string Clip(string text)
    {
        return text.Length <= 60 ? text : text[..60] + "…";
    }

    #endregion

    #region IDisposable

    /// <summary>Closes the stream when the reader owns it.</summary>
    public void Dispose()
    {
        if (m_ownsStream)
            m_stream.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>Records skipped because their payload exceeded the cap.</summary>
    public int SkippedOversized => m_skippedOversized;

    /// <summary>Whether the input ended inside a record (a partial download); the records before it were returned.</summary>
    public bool Truncated => m_truncated;

    /// <summary>Bytes of decompressed input consumed so far.</summary>
    public long BytesRead => m_offset;

    #endregion
}

/// <summary>Malformed WARC input at a known offset.</summary>
public sealed class WarcFormatException(string message) : FormatException(message);
