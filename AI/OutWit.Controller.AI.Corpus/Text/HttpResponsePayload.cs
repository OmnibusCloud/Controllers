using System.Text;
using System.Text.RegularExpressions;

namespace OutWit.Controller.AI.Corpus.Text;

/// <summary>
/// The HTTP response a WARC response record carries: the status line, the headers and the
/// body, with the content type and the charset read off the headers (and, failing that, off a
/// meta tag in the body's first kilobytes). Decoding is explicit so that it is the same on
/// every node: a named charset the host knows, else UTF-8, never the platform default.
/// </summary>
public sealed partial class HttpResponsePayload
{
    #region Constants

    private const int META_SNIFF_BYTES = 4096;
    private const string CONTENT_TYPE = "Content-Type";

    #endregion

    #region Fields

    private static readonly Encoding s_utf8 = new UTF8Encoding(false, throwOnInvalidBytes: false);

    #endregion

    #region Constructors

    static HttpResponsePayload()
    {
        // Legacy charsets (windows-1251, gb2312, iso-8859-x, ...) are common on the public web.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private HttpResponsePayload(string statusLine, IReadOnlyList<KeyValuePair<string, string>> headers, ReadOnlyMemory<byte> body)
    {
        StatusLine = statusLine;
        Headers = headers;
        Body = body;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Splits a response record payload into status line, headers and body.
    /// </summary>
    /// <param name="payload">The WARC response record payload.</param>
    /// <returns>The parsed response, or null when the payload is not an HTTP response.</returns>
    public static HttpResponsePayload? Parse(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        var headerEnd = IndexOfBlankLine(span, out var separatorLength);
        if (headerEnd < 0)
            return null;

        var headerText = Encoding.Latin1.GetString(span[..headerEnd]);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0 || !lines[0].StartsWith("HTTP/", StringComparison.Ordinal))
            return null;

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            headers.Add(new KeyValuePair<string, string>(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }

        return new HttpResponsePayload(lines[0], headers, payload[(headerEnd + separatorLength)..]);
    }

    /// <summary>
    /// The value of a header, or null (first occurrence wins; names are case-insensitive).
    /// </summary>
    /// <param name="name">The header name.</param>
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

    /// <summary>
    /// Decodes the body as text: the header charset, else a meta charset in the first kilobytes,
    /// else UTF-8. Undecodable bytes become U+FFFD, never an exception.
    /// </summary>
    /// <returns>The body text.</returns>
    public string DecodeBody()
    {
        var encoding = ResolveEncoding(Charset) ?? ResolveEncoding(SniffMetaCharset()) ?? s_utf8;
        return encoding.GetString(Body.Span);
    }

    private string? SniffMetaCharset()
    {
        var head = Encoding.Latin1.GetString(Body.Span[..Math.Min(META_SNIFF_BYTES, Body.Length)]);
        var meta = MetaCharset().Match(head);
        return meta.Success ? meta.Groups[1].Value : null;
    }

    private static Encoding? ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
            return null;

        var name = charset.Trim().Trim('"', '\'');
        if (name.Equals("utf8", StringComparison.OrdinalIgnoreCase) || name.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            return s_utf8;

        try
        {
            // Decoder fallback: replacement, so a wrong label degrades the text instead of failing the document.
            return Encoding.GetEncoding(name, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static int IndexOfBlankLine(ReadOnlySpan<byte> span, out int separatorLength)
    {
        var crlf = span.IndexOf("\r\n\r\n"u8);
        var lf = span.IndexOf("\n\n"u8);
        if (crlf >= 0 && (lf < 0 || crlf <= lf))
        {
            separatorLength = 4;
            return crlf;
        }

        separatorLength = lf >= 0 ? 2 : 0;
        return lf;
    }

    [GeneratedRegex("""<meta[^>]+charset\s*=\s*["']?\s*([A-Za-z0-9_\-:.]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharset();

    [GeneratedRegex(@"charset\s*=\s*([^;\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderCharset();

    #endregion

    #region Properties

    /// <summary>The status line, e.g. "HTTP/1.1 200 OK".</summary>
    public string StatusLine { get; }

    /// <summary>The headers in wire order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

    /// <summary>The body bytes.</summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>The status code, or 0 when the status line does not carry one.</summary>
    public int StatusCode => StatusLine.Split(' ') is { Length: > 1 } parts && int.TryParse(parts[1], out var code) ? code : 0;

    /// <summary>The media type of the body (lower-case, no parameters), or "" when absent.</summary>
    public string MediaType
    {
        get
        {
            var contentType = Header(CONTENT_TYPE);
            if (contentType == null)
                return "";

            var semicolon = contentType.IndexOf(';');
            return (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim().ToLowerInvariant();
        }
    }

    /// <summary>The charset the Content-Type header names, or null.</summary>
    public string? Charset
    {
        get
        {
            var contentType = Header(CONTENT_TYPE);
            if (contentType == null)
                return null;

            var match = HeaderCharset().Match(contentType);
            return match.Success ? match.Groups[1].Value.Trim('"', '\'') : null;
        }
    }

    /// <summary>Whether the body is HTML (text/html or XHTML).</summary>
    public bool IsHtml => MediaType is "text/html" or "application/xhtml+xml";

    #endregion
}
