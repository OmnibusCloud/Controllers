using System.Text;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Corpus.Text.Casing;

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

    /// <summary>
    /// The most bytes of a body that are decoded: an HTML page is read for its first 4 Mi
    /// characters (<see cref="HtmlTextExtractor.MAX_HTML_CHARS"/>), which no charset writes in
    /// more than four bytes each. A record may hold 64 MiB; decoding all of it would make a
    /// string of twice that to throw most of it away.
    /// </summary>
    public const int MAX_DECODE_BYTES = 4 * HtmlTextExtractor.MAX_HTML_CHARS;

    private const int META_SNIFF_BYTES = 4096;
    private const string CONTENT_TYPE = "Content-Type";
    private const int UTF7_CODE_PAGE = 65000;

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
            if (CorpusCasing.EqualsIgnoreAsciiCase(key, name))
                return value;
        }

        return null;
    }

    /// <summary>
    /// Decodes the body as text: the header charset, else a meta charset in the first kilobytes,
    /// else UTF-8. Undecodable bytes become U+FFFD, never an exception. A page whose charset has
    /// a name the platform knows and does not decode (UTF-7, which .NET refuses) has no text: it
    /// is one page, and it must not cost the shard that holds it.
    /// </summary>
    /// <param name="text">The body text (its first <see cref="MAX_DECODE_BYTES"/> bytes: more than an HTML page is read for); "" when the charset is unsupported.</param>
    /// <returns>False when the page names a charset that cannot be decoded.</returns>
    public bool TryDecodeBody(out string text)
    {
        text = "";

        var encoding = ResolveEncoding(Charset, out var unsupported);
        if (encoding == null && !unsupported)
            encoding = ResolveEncoding(SniffMetaCharset(), out unsupported);
        if (unsupported)
            return false;

        var body = Body.Span[..Math.Min(Body.Length, MAX_DECODE_BYTES)];
        try
        {
            text = (encoding ?? s_utf8).GetString(body);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidOperationException)
        {
            // A decoder that gives up on these bytes: the page's charset is not one that reads them.
            return false;
        }
    }

    /// <summary>Decodes the body as text like <see cref="TryDecodeBody"/>; "" for a page whose charset cannot be decoded.</summary>
    /// <returns>The body text.</returns>
    public string DecodeBody()
    {
        return TryDecodeBody(out var text) ? text : "";
    }

    private string? SniffMetaCharset()
    {
        var head = Encoding.Latin1.GetString(Body.Span[..Math.Min(META_SNIFF_BYTES, Body.Length)]);
        var meta = MetaCharset().Match(head);
        return meta.Success ? meta.Groups[1].Value : null;
    }

    /// <summary>
    /// The encoding of a charset name. Three answers: an encoding; none, for no name or a name
    /// nobody knows (the next source is asked, as before); or unsupported, for a name the
    /// platform knows and refuses to decode. The answer is the same on every node: the names and
    /// the tables are the runtime's own, and UTF-7 is refused whatever the host's switch says.
    /// </summary>
    private static Encoding? ResolveEncoding(string? charset, out bool unsupported)
    {
        unsupported = false;
        if (string.IsNullOrWhiteSpace(charset))
            return null;

        var name = charset.Trim().Trim('"', '\'');
        if (CorpusCasing.EqualsIgnoreAsciiCase(name, "utf8") || CorpusCasing.EqualsIgnoreAsciiCase(name, "utf-8"))
            return s_utf8;

        try
        {
            // Decoder fallback: replacement, so a wrong label degrades the text instead of failing the document.
            var encoding = Encoding.GetEncoding(name, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            if (encoding.CodePage != UTF7_CODE_PAGE)
                return encoding;

            // A host may switch UTF-7 back on; the product must not depend on it.
            unsupported = true;
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            unsupported = true;
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

    [GeneratedRegex("""<meta[^>]+charset\s*=\s*["']?\s*([A-Za-z0-9_\-:.]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetaCharset();

    [GeneratedRegex(@"charset\s*=\s*([^;\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
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
            return CorpusCasing.ToLowerAscii((semicolon >= 0 ? contentType[..semicolon] : contentType).Trim());
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
