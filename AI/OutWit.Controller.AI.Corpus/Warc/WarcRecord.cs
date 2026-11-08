namespace OutWit.Controller.AI.Corpus.Warc;

/// <summary>
/// One WARC record: its header fields (names as written, matched case-insensitively) and its
/// payload bytes. WET conversion records carry plain text; WARC response records carry the
/// HTTP response (headers and body); request, metadata and warcinfo records carry their own.
/// </summary>
public sealed class WarcRecord
{
    #region Constants

    private const string TYPE = "WARC-Type";
    private const string TARGET_URI = "WARC-Target-URI";
    private const string RECORD_ID = "WARC-Record-ID";
    private const string DATE = "WARC-Date";
    private const string CONTENT_TYPE = "Content-Type";
    private const string IDENTIFIED_LANGUAGE = "WARC-Identified-Content-Language";

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a record.
    /// </summary>
    /// <param name="version">The WARC version line, e.g. "WARC/1.0".</param>
    /// <param name="headers">The header fields in file order.</param>
    /// <param name="payload">The payload bytes (exactly Content-Length of them).</param>
    /// <param name="offset">The byte offset of the record in the decompressed stream.</param>
    public WarcRecord(string version, IReadOnlyList<KeyValuePair<string, string>> headers, ReadOnlyMemory<byte> payload, long offset)
    {
        Version = version;
        Headers = headers;
        Payload = payload;
        Offset = offset;
    }

    #endregion

    #region Functions

    /// <summary>
    /// The value of a header field, or null when absent (the first occurrence wins).
    /// </summary>
    /// <param name="name">The field name, case-insensitive.</param>
    /// <returns>The value, or null.</returns>
    public string? Header(string name)
    {
        foreach (var (key, value) in Headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    #endregion

    #region Properties

    /// <summary>The WARC version line.</summary>
    public string Version { get; }

    /// <summary>The header fields in file order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

    /// <summary>The payload bytes.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Byte offset of the record in the decompressed stream.</summary>
    public long Offset { get; }

    /// <summary>The record type (warcinfo, request, response, metadata, conversion, ...), or "" when missing.</summary>
    public string Type => Header(TYPE) ?? "";

    /// <summary>The target URI, or null for records without one (warcinfo).</summary>
    public string? TargetUri => Header(TARGET_URI);

    /// <summary>The record id (a urn:uuid), or null.</summary>
    public string? RecordId => Header(RECORD_ID);

    /// <summary>The record date, or null.</summary>
    public string? Date => Header(DATE);

    /// <summary>The payload's content type, or null.</summary>
    public string? ContentType => Header(CONTENT_TYPE);

    /// <summary>The language Common Crawl identified for the document (ISO 639-3 codes, comma-separated), or null.</summary>
    public string? IdentifiedLanguage => Header(IDENTIFIED_LANGUAGE);

    #endregion
}
