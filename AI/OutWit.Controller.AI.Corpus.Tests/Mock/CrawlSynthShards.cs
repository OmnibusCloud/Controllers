using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The generated crawl as the two shards the tests read, written the way Common Crawl writes
/// its files: a WARC file of captures (a <c>warcinfo</c> record, then a request, a response and
/// a metadata record for each capture, one gzip member a record) and the WET file of the same
/// crawl (a <c>warcinfo</c> record, then a conversion record with the text of each page).
/// <para>
/// The WARC shard holds the first pages of the crawl as HTML — in the charsets of the pages,
/// named in the header, in a meta tag or nowhere, some with their elements left open — and
/// between them the captures that are no page: a robots file, an image, a style sheet, a JSON
/// answer, a site map, a page that is gone, a redirect, a PDF of the tests' own making, a page in
/// a charset no runtime decodes, a page with bytes its charset does not have, and a record that
/// is no HTTP response at all.
/// </para>
/// <para>
/// Every byte of a record is a function of the generator: the record ids are derived, the date
/// is fixed. Only the gzip container is the build machine's; the committed shards are held to
/// the records, not to their compression.
/// </para>
/// </summary>
public static class CrawlSynthShards
{
    #region Constants

    /// <summary>How many pages of the crawl the WARC shard holds: the head of the WET shard's.</summary>
    public const int WARC_PAGES = 110;

    private const string DATE = "2026-10-05T10:56:23Z";
    private const string HTTP_DATE = "Mon, 05 Oct 2026 10:56:23 GMT";
    private const string HOST = "https://valley-courier.example/";
    private const string SOFTWARE = "OutWit.Controller.AI.Corpus.Tests CrawlSynth";
    private const string DESCRIPTION = "a generated shard of the Corpus controller's tests: every page is written by the tests' own generator, none is crawled";
    private const int PDF_SENTENCES = 10;

    #endregion

    #region Functions

    /// <summary>The WET shard: gzip, a member per record.</summary>
    /// <param name="fixtures">The fixtures folder.</param>
    /// <returns>The shard's bytes.</returns>
    public static byte[] Wet(string fixtures)
    {
        return Pack(WetRecords(fixtures));
    }

    /// <summary>The WARC shard: gzip, a member per record.</summary>
    /// <param name="fixtures">The fixtures folder.</param>
    /// <returns>The shard's bytes.</returns>
    public static byte[] Warc(string fixtures)
    {
        return Pack(WarcRecords(fixtures));
    }

    /// <summary>Records as a shard: each a gzip member of its own, one after another, as Common Crawl writes its files.</summary>
    /// <param name="records">The records.</param>
    /// <returns>The shard's bytes.</returns>
    public static byte[] Pack(IEnumerable<byte[]> records)
    {
        using var output = new MemoryStream();
        foreach (var record in records)
        {
            using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
            gzip.Write(record);
        }

        return output.ToArray();
    }

    /// <summary>The records of the WET shard, in order, as they are before compression.</summary>
    /// <param name="fixtures">The fixtures folder.</param>
    /// <returns>The records.</returns>
    public static IReadOnlyList<byte[]> WetRecords(string fixtures)
    {
        var ids = new CrawlSynthIds("wet");
        var records = new List<byte[]> { Info(ids, "generated-web.warc.wet.gz") };

        foreach (var page in CrawlSynth.Pages(fixtures))
        {
            var headers = new List<string> { $"WARC-Refers-To: {ids.Next()}" };
            if (page.Label != null)
                headers.Add($"WARC-Identified-Content-Language: {page.Label}");

            records.Add(Record(ids, "conversion", page.Url, "text/plain", Encoding.UTF8.GetBytes(Text(page)), headers));
        }

        return records;
    }

    /// <summary>The records of the WARC shard, in order, as they are before compression.</summary>
    /// <param name="fixtures">The fixtures folder.</param>
    /// <returns>The records.</returns>
    public static IReadOnlyList<byte[]> WarcRecords(string fixtures)
    {
        var ids = new CrawlSynthIds("warc");
        var records = new List<byte[]> { Info(ids, "generated-web.warc.gz") };
        var pages = CrawlSynth.Pages(fixtures);
        var others = Others(fixtures);

        for (var index = 0; index < Math.Min(WARC_PAGES, pages.Count); index++)
        {
            var page = pages[index];
            var body = Encode(page, Html(page));
            var contentType = page.CharsetPlace is "header" or "references" ? $"text/html; charset={page.Charset}" : "text/html";
            Capture(records, ids, page.Url, 200, "OK", contentType, body, "text/html");

            foreach (var other in others.Where(other => other.After == index))
                Capture(records, ids, other.Url, other.Status, other.Reason, other.ContentType, other.Body, other.Identified, other.Raw);
        }

        return records;
    }

    /// <summary>The text of a page as the crawl's own conversion gives it: every block a line, the menu and the foot among them.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The text; empty for a page without a word.</returns>
    public static string Text(CrawlSynthPage page)
    {
        var lines = new List<string>();
        if (page.Title.Length > 0)
            lines.Add(page.Title);

        lines.AddRange(page.Blocks.Select(block => block.Text));

        return lines.Count == 0 ? "" : string.Join('\n', lines) + "\n";
    }

    /// <summary>The page as its site serves it.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The HTML.</returns>
    public static string Html(CrawlSynthPage page)
    {
        var references = page.CharsetPlace == "references";
        var close = !page.Unclosed;
        var html = new StringBuilder();

        string Escape(string text)
        {
            var escaped = new StringBuilder(text.Length);
            foreach (var rune in text.EnumerateRunes())
            {
                if (rune.Value == '&')
                    escaped.Append("&amp;");
                else if (rune.Value == '<')
                    escaped.Append("&lt;");
                else if (rune.Value == '>')
                    escaped.Append("&gt;");
                else if (references && rune.Value > 0x7E)
                    escaped.Append("&#").Append(rune.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
                else
                    escaped.Append(rune.ToString());
            }

            return escaped.ToString();
        }

        void Group(CrawlSynthRole role, string open, string itemOpen, string itemClose, string end)
        {
            var items = page.Blocks.Where(block => block.Role == role).ToList();
            if (items.Count == 0)
                return;

            html.Append(open).Append('\n');
            foreach (var (_, text) in items)
                html.Append(itemOpen).Append(Escape(text)).Append(close ? itemClose : "").Append('\n');
            html.Append(end).Append('\n');
        }

        html.Append("<!DOCTYPE html>\n<html>\n<head>\n");
        if (page.CharsetPlace == "meta")
            html.Append(page.Charset == "utf-8" ? "<meta charset=\"utf-8\">\n" : $"<meta http-equiv=\"Content-Type\" content=\"text/html; charset={page.Charset}\">\n");
        html.Append("<title>").Append(Escape(page.Title)).Append("</title>\n");
        html.Append("<style>body { font-family: serif; } nav li { display: inline; } .more { color: #555; }</style>\n");
        html.Append("<script>var page = { kind: \"").Append(page.Kind).Append("\", wide: 1 < 2 && 3 > 2 };</script>\n");
        html.Append("</head>\n<body>\n");

        Group(CrawlSynthRole.Menu, "<nav><ul>", "<li><a href=\"/\">", "</a></li>", "</ul></nav>");

        html.Append("<main>\n");
        foreach (var (role, text) in page.Blocks)
        {
            if (role == CrawlSynthRole.Heading)
                html.Append("<h1>").Append(Escape(text)).Append("</h1>\n");
            else if (role == CrawlSynthRole.Paragraph)
                html.Append("<p>").Append(Escape(text)).Append(close ? "</p>" : "").Append('\n');
        }

        Group(CrawlSynthRole.Item, "<ul>", "<li>", "</li>", "</ul>");
        html.Append("</main>\n");

        Group(CrawlSynthRole.Teaser, "<aside class=\"more\"><ul>", "<li><a href=\"/more\">", "</a></li>", "</ul></aside>");
        Group(CrawlSynthRole.Footer, "<footer>", "<p>", "</p>", "</footer>");

        html.Append("<div hidden>").Append(Escape(page.Url)).Append("</div>\n");
        html.Append("<script>document.title = document.title;</script>\n");
        if (close)
            html.Append("</body>\n</html>\n");

        return html.ToString();
    }

    /// <summary>The captures of the WARC shard that are no page of the crawl, each after the page of an index.</summary>
    private static List<(int After, string Url, int Status, string Reason, string ContentType, byte[] Body, string? Identified, byte[]? Raw)> Others(string fixtures)
    {
        var english = CrawlSynthLanguage.Load(fixtures, "eng");
        var random = new CrawlSynthRandom(CrawlSynth.SEED + 1);
        var prose = english.Compose(random, PDF_SENTENCES);
        var gone = "<html><head><title>Not found</title></head><body><p>" + string.Join(' ', english.Compose(random, 6)) + "</p></body></html>";
        var seven = "<html><head><title>+AGgAaQ-</title></head><body><p>" + string.Join(' ', english.Compose(random, 6)) + "</p></body></html>";
        var broken = Encoding.UTF8.GetBytes("<html><head><title>Bytes</title></head><body><p>" + string.Join(' ', english.Compose(random, 8)) + " The last word is cut: caf");

        return
        [
            (4, HOST + "robots.txt", 200, "OK", "text/plain", Encoding.ASCII.GetBytes("User-agent: *\nDisallow: /private/\n"), "text/plain", null),
            (11, HOST + "images/quay.png", 200, "OK", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0], "image/png", null),
            (19, HOST + "api/tide.json", 200, "OK", "application/json", Encoding.ASCII.GetBytes("{\"station\": \"lower lock\", \"level\": 1.42, \"rising\": true}\n"), "application/json", null),
            (30, HOST + "papers/harbour-report.pdf", 200, "OK", "application/pdf", PdfSynth.Document([prose.SelectMany(Halves).ToList()]), "application/pdf", null),
            (39, HOST + "en/gone.html", 404, "Not Found", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(gone), "text/html", null),
            (46, HOST + "old-address", 301, "Moved Permanently", "text/html", [], null, null),
            (54, HOST + "style/site.css", 200, "OK", "text/css", Encoding.ASCII.GetBytes("body { margin: 2em; }\nnav li { display: inline; }\n"), "text/css", null),
            (62, HOST + "en/seven.html", 200, "OK", "text/html; charset=utf-7", Encoding.ASCII.GetBytes(seven), "text/html", null),
            (69, HOST + "sitemap.xml", 200, "OK", "application/xml", Encoding.ASCII.GetBytes("<?xml version=\"1.0\"?>\n<urlset><url><loc>" + HOST + "</loc></url></urlset>\n"), "application/xml", null),
            (77, HOST + "en/cut.html", 200, "OK", "text/html; charset=utf-8", [.. broken, 0xC3], "text/html", null),
            (84, HOST + "en/strict.xhtml", 200, "OK", "application/xhtml+xml; charset=utf-8", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Strict</title></head><body><p>" + string.Join(' ', english.Compose(random, 7)) + "</p></body></html>"), "application/xhtml+xml", null),
            (91, HOST + "en/noise.html", 200, "OK", "text/html", [], "text/html", Encoding.ASCII.GetBytes("this capture is no HTTP response: it has no status line and no blank line"))
        ];
    }

    /// <summary>A sentence as two lines of a PDF page.</summary>
    private static IEnumerable<string> Halves(string sentence)
    {
        var words = sentence.Split(' ');
        var half = words.Length / 2;
        return [string.Join(' ', words.Take(half)), string.Join(' ', words.Skip(half))];
    }

    private static byte[] Encode(CrawlSynthPage page, string html)
    {
        return page.CharsetPlace == "references" ? Encoding.ASCII.GetBytes(html) : Encoding.GetEncoding(page.Charset).GetBytes(html);
    }

    /// <summary>One capture as a crawl keeps it: the request, the response, the metadata.</summary>
    private static void Capture(List<byte[]> records, CrawlSynthIds ids, string url, int status, string reason, string contentType, byte[] body, string? identified, byte[]? raw = null)
    {
        var uri = new Uri(url);
        var request = Encoding.ASCII.GetBytes($"GET {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Host}\r\nAccept: text/html\r\nUser-Agent: CrawlSynth\r\n\r\n");
        records.Add(Record(ids, "request", url, "application/http; msgtype=request", request, []));

        var head = new StringBuilder($"HTTP/1.1 {status} {reason}\r\nDate: {HTTP_DATE}\r\nServer: synth\r\nContent-Type: {contentType}\r\n");
        if (status == 301)
            head.Append($"Location: {HOST}\r\n");
        head.Append(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n\r\n");

        var headers = new List<string> { $"WARC-Concurrent-To: {ids.Last}", "WARC-IP-Address: 192.0.2.17" };
        if (identified != null)
            headers.Add($"WARC-Identified-Payload-Type: {identified}");

        records.Add(Record(ids, "response", url, "application/http; msgtype=response", raw ?? [.. Encoding.ASCII.GetBytes(head.ToString()), .. body], headers));
        records.Add(Record(ids, "metadata", url, "application/warc-fields", Encoding.ASCII.GetBytes("fetchTimeMs: 120\r\n"), [$"WARC-Concurrent-To: {ids.Last}"]));
    }

    private static byte[] Info(CrawlSynthIds ids, string fileName)
    {
        var fields = Encoding.ASCII.GetBytes($"software: {SOFTWARE}\r\ndescription: {DESCRIPTION}\r\nformat: WARC File Format 1.0\r\n");
        return Record(ids, "warcinfo", null, "application/warc-fields", fields, [$"WARC-Filename: {fileName}"]);
    }

    private static byte[] Record(CrawlSynthIds ids, string type, string? uri, string contentType, byte[] payload, IReadOnlyList<string> extraHeaders)
    {
        var header = new StringBuilder("WARC/1.0\r\n");
        header.Append($"WARC-Type: {type}\r\n");
        if (uri != null)
            header.Append($"WARC-Target-URI: {uri}\r\n");
        header.Append($"WARC-Date: {DATE}\r\n");
        header.Append($"WARC-Record-ID: {ids.Next()}\r\n");
        foreach (var line in extraHeaders)
            header.Append(line).Append("\r\n");
        header.Append($"Content-Type: {contentType}\r\n");
        header.Append(CultureInfo.InvariantCulture, $"Content-Length: {payload.Length}\r\n\r\n");

        return [.. Encoding.UTF8.GetBytes(header.ToString()), .. payload, .. "\r\n\r\n"u8];
    }

    #endregion
}
