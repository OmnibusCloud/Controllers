using System.IO.Compression;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// Writes WARC/WET files the way Common Crawl does — one gzip member per record — from
/// synthetic documents, so the pipeline can be driven at any size and with any defect
/// without committing large fixtures.
/// </summary>
public static class WarcSynth
{
    #region Constants

    private static readonly string[] WORDS =
    [
        "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "while", "seventeen", "engineers", "measure",
        "throughput", "across", "distributed", "nodes", "under", "deterministic", "pipelines", "and", "careful", "limits",
        "because", "every", "shard", "must", "reproduce", "its", "product", "exactly", "on", "any", "machine", "today"
    ];

    #endregion

    #region Functions

    /// <summary>A WET conversion record with the given text.</summary>
    public static byte[] ConversionRecord(string uri, string text)
    {
        return Record("conversion", uri, "text/plain", Encoding.UTF8.GetBytes(text));
    }

    /// <summary>A WARC response record carrying an HTTP 200 HTML response.</summary>
    public static byte[] HtmlResponseRecord(string uri, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n\r\n");
        return Record("response", uri, "application/http; msgtype=response", [.. head, .. body]);
    }

    /// <summary>
    /// A WARC response record carrying an HTTP 200 response with a PDF body, the way Common Crawl
    /// stores one: the server's Content-Type, Common Crawl's identified payload type, and
    /// <c>WARC-Truncated: length</c> when the body was cut at the crawler's limit.
    /// </summary>
    /// <param name="uri">The target URI.</param>
    /// <param name="body">The body as stored (cut short already when truncated).</param>
    /// <param name="truncated">Whether the record says the crawler cut it.</param>
    /// <param name="contentType">The server's Content-Type.</param>
    /// <param name="identifiedType">Common Crawl's identified payload type, or null for none.</param>
    /// <returns>The record.</returns>
    public static byte[] PdfResponseRecord(string uri, byte[] body, bool truncated = false, string contentType = "application/pdf", string? identifiedType = "application/pdf")
    {
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n\r\n");
        var extra = new List<string>();
        if (identifiedType != null)
            extra.Add($"WARC-Identified-Payload-Type: {identifiedType}");
        if (truncated)
            extra.Add("WARC-Truncated: length");

        return Record("response", uri, "application/http; msgtype=response", [.. head, .. body], extra);
    }

    /// <summary>A record of any type with a raw payload.</summary>
    public static byte[] Record(string type, string uri, string contentType, byte[] payload)
    {
        return Record(type, uri, contentType, payload, []);
    }

    /// <summary>A record of any type with a raw payload and further header lines.</summary>
    public static byte[] Record(string type, string uri, string contentType, byte[] payload, IReadOnlyList<string> extraHeaders)
    {
        var header = Encoding.ASCII.GetBytes(
            "WARC/1.0\r\n" +
            $"WARC-Type: {type}\r\n" +
            $"WARC-Target-URI: {uri}\r\n" +
            "WARC-Date: 2026-10-05T10:56:23Z\r\n" +
            $"WARC-Record-ID: <urn:uuid:{Guid.NewGuid():D}>\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            string.Concat(extraHeaders.Select(line => line + "\r\n")) +
            "\r\n");

        return [.. header, .. payload, .. Encoding.ASCII.GetBytes("\r\n\r\n")];
    }

    /// <summary>Gzips each record as its own member and concatenates them, as Common Crawl does.</summary>
    public static byte[] Shard(IEnumerable<byte[]> records)
    {
        using var output = new MemoryStream();
        foreach (var record in records)
        {
            using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
            gzip.Write(record);
        }

        return output.ToArray();
    }

    /// <summary>Writes a shard of synthetic English documents to a file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="documents">How many records.</param>
    /// <param name="wordsPerDocument">Words per document (the quality filter wants 50 or more).</param>
    /// <param name="seed">Seed of the text generator.</param>
    public static void WriteWetShard(string path, int documents, int wordsPerDocument, int seed)
    {
        var random = new Random(seed);
        using var output = File.Create(path);
        for (var i = 0; i < documents; i++)
        {
            var record = ConversionRecord($"https://synthetic.example/{seed}/{i}", Paragraphs(random, wordsPerDocument));
            using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
            gzip.Write(record);
        }
    }

    /// <summary>Synthetic English-looking text: distinct sentences, so the repetition rules pass.</summary>
    public static string Paragraphs(Random random, int words)
    {
        var builder = new StringBuilder(words * 7);
        var inSentence = 0;
        for (var i = 0; i < words; i++)
        {
            var word = WORDS[random.Next(WORDS.Length)];
            if (inSentence == 0)
                word = char.ToUpperInvariant(word[0]) + word[1..];
            builder.Append(word);
            inSentence++;
            if (inSentence >= 8 + random.Next(9))
            {
                builder.Append(random.Next(6) == 0 ? ".\n" : ". ");
                inSentence = 0;
            }
            else
                builder.Append(' ');
        }

        builder.Append("End.");
        return builder.ToString();
    }

    #endregion
}
