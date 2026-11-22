using System.Text;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// Audit finding H-5: one page that names <c>charset=utf-7</c> (a charset .NET knows and refuses
/// to decode) threw out of the shard's processing, and the whole WARC shard ended <c>Failed</c> on
/// every node. It is one page without text: it drops, and the shard is made.
/// </summary>
[TestFixture]
public sealed class ShardProcessorH5Tests
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        // The tests encode pages in legacy charsets themselves.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    #endregion

    #region Tests

    [TestCase("header")]
    [TestCase("meta")]
    public void H5APageInUtf7DropsAndTheShardIsMadeTest(string where)
    {
        var good = WarcSynth.HtmlResponseRecord("https://a.example/good", Page(Prose(400)));
        var shard = WarcSynth.Shard([good, Poison(where, "utf-7"), good]);

        var product = new ShardProcessor(Plain(), null).Process(new MemoryStream(shard), CorpusSourceFormat.Warc, new MemoryStream(), new MemoryStream());
        var stats = product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.DocumentsOut, Is.EqualTo(2), "the pages around it are documents");
            Assert.That(stats.Drops.Select(drop => (drop.Name, drop.Count)), Is.EqualTo(new[] { ("unsupported_charset", 1) }));
            Assert.That(ShardDropRules.StageOf("unsupported_charset"), Is.EqualTo(ShardDropRules.STAGE_EXTRACT));
        });
    }

    [Test]
    public void H5APageTheParserFailsOnDropsAndTheShardIsMadeTest()
    {
        // The finding's general form: whatever a decoder or the parser throws over one page's
        // bytes is that page's drop. The parser stands in here; no input is known that makes the
        // real one fail.
        var good = WarcSynth.HtmlResponseRecord("https://a.example/good", Page(Prose(400)));
        var poison = WarcSynth.HtmlResponseRecord("https://a.example/poison", Page("POISON " + Prose(400)));
        var shard = WarcSynth.Shard([good, poison, good]);
        var processor = new ShardProcessor(Plain(), null) { HtmlReader = html => html.Contains("POISON", StringComparison.Ordinal) ? throw new InvalidOperationException("the parser's own failure") : Read(html) };

        var product = processor.Process(new MemoryStream(shard), CorpusSourceFormat.Warc, new MemoryStream(), new MemoryStream());
        var stats = product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.DocumentsOut, Is.EqualTo(2));
            Assert.That(stats.Drops.Select(drop => (drop.Name, drop.Count)), Is.EqualTo(new[] { ("html_error", 1) }));
            Assert.That(ShardDropRules.StageOf("html_error"), Is.EqualTo(ShardDropRules.STAGE_EXTRACT));
        });
    }

    [Test]
    public void H5WhatIsTheNodesOwnIsNeverADropTest()
    {
        // A node whose parser does not load, or that is out of memory, must not report a shard
        // of dropped pages: these go up and the shard comes back as a node fault.
        var shard = WarcSynth.Shard([WarcSynth.HtmlResponseRecord("https://a.example/page", Page(Prose(400)))]);
        Exception[] nodes =
        [
            new TypeLoadException("the parser's assembly is another version"),
            new FileNotFoundException("the parser's assembly is missing"),
            new MissingMethodException("the parser's assembly is another version"),
            new OutOfMemoryException(),
            new PlatformNotSupportedException(),
            new ObjectDisposedException("parser"),
            new IOException("the disk")
        ];

        Assert.Multiple(() =>
        {
            foreach (var fault in nodes)
            {
                var processor = new ShardProcessor(Plain(), null) { HtmlReader = _ => throw fault };
                var thrown = Assert.Catch(() => processor.Process(new MemoryStream(shard), CorpusSourceFormat.Warc, new MemoryStream(), new MemoryStream()));
                Assert.That(thrown, Is.SameAs(fault), fault.GetType().Name);
            }
        });
    }

    [TestCase("utf-7")]
    [TestCase("UTF-7")]
    [TestCase("unicode-1-1-utf-7")]
    [TestCase("csUnicode11UTF7")]
    [TestCase("x-unicode-2-0-utf-7")]
    public void H5EveryNameOfUtf7IsUnsupportedTest(string charset)
    {
        var payload = Payload("text/html; charset=" + charset, "<p>text</p>");

        Assert.Multiple(() =>
        {
            Assert.That(payload.TryDecodeBody(out var text), Is.False);
            Assert.That(text, Is.Empty);
            Assert.That(payload.DecodeBody(), Is.Empty);
        });
    }

    [Test]
    public void H5ACharsetNobodyKnowsStillReadsAsBeforeTest()
    {
        // A name that is no charset falls back to the page's meta tag, then to UTF-8, as it
        // always did: only a charset that is known and refused has no text.
        var nonsense = Payload("text/html; charset=x-no-such-charset", "<p>café</p>");
        var cyrillic = Payload("text/html; charset=windows-1251", "<p>привет</p>", Encoding.GetEncoding(1251));
        var metaWins = Payload("text/html; charset=x-no-such-charset", "<meta charset=\"windows-1251\"><p>привет</p>", Encoding.GetEncoding(1251));

        Assert.Multiple(() =>
        {
            Assert.That(nonsense.TryDecodeBody(out var first), Is.True);
            Assert.That(first, Does.Contain("café"));
            Assert.That(cyrillic.TryDecodeBody(out var second), Is.True);
            Assert.That(second, Does.Contain("привет"));
            Assert.That(metaWins.TryDecodeBody(out var third), Is.True);
            Assert.That(third, Does.Contain("привет"));
        });
    }

    [Test]
    public void H5OnlyTheStartOfAHugeBodyIsDecodedTest()
    {
        // A record may hold 64 MiB; a page is read for its first 4 Mi characters.
        var body = new byte[HttpResponsePayload.MAX_DECODE_BYTES + (1 << 20)];
        Array.Fill(body, (byte)'a');
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\r\n");
        var payload = HttpResponsePayload.Parse(new ReadOnlyMemory<byte>([.. head, .. body]))!;

        Assert.Multiple(() =>
        {
            Assert.That(payload.TryDecodeBody(out var text), Is.True);
            Assert.That(text.Length, Is.EqualTo(HttpResponsePayload.MAX_DECODE_BYTES));
        });
    }

    #endregion

    #region Tools

    private static (HtmlReading Reading, string Text) Read(string html)
    {
        var reading = HtmlTextExtractor.Read(html, out var text);
        return (reading, text);
    }

    private static CorpusPipelineData Plain()
    {
        return new CorpusPipelineData { Version = "h5", IdentifyLanguage = false, ExtractPdf = false, CleanLines = false, FilterQuality = false };
    }

    private static string Prose(int words)
    {
        return WarcSynth.Paragraphs(new Random(7), words);
    }

    private static string Page(string prose)
    {
        return "<html><body><p>" + prose + "</p></body></html>";
    }

    private static byte[] Poison(string where, string charset)
    {
        var html = where == "meta"
            ? "<html><head><meta charset=\"" + charset + "\"></head><body><p>" + Prose(400) + "</p></body></html>"
            : Page(Prose(400));
        var body = Encoding.ASCII.GetBytes(html);
        var contentType = where == "header" ? "text/html; charset=" + charset : "text/html";
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n\r\n");
        return WarcSynth.Record("response", "https://a.example/poison", "application/http; msgtype=response", [.. head, .. body]);
    }

    private static HttpResponsePayload Payload(string contentType, string html, Encoding? encoding = null)
    {
        var body = (encoding ?? Encoding.UTF8).GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\n\r\n");
        return HttpResponsePayload.Parse(new ReadOnlyMemory<byte>([.. head, .. body]))!;
    }

    #endregion
}
