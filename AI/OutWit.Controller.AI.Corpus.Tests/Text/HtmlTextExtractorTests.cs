using OutWit.Controller.AI.Corpus.Tests.Mock;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Text;

/// <summary>
/// HTML to text on the generated WARC shard, on the head of a real WARC file (the cases of the
/// category <c>Crawl</c>: fetched by range, ignored where the origin cannot be reached) and on
/// hostile pages: every HTML response decodes and extracts, to the same text on every machine,
/// scripts and navigation gone, the title first, and our text agrees with the crawl's own WET
/// conversion of the same pages well beyond chance.
/// </summary>
[TestFixture]
public sealed class HtmlTextExtractorTests
{
    #region Constants

    private const string WARC_FIXTURE = CorpusFixtures.WARC;
    private const string WET_FIXTURE = CorpusFixtures.WET;
    private const string CRAWL_WET = CorpusFixtures.CRAWL_WET;
    private const string CRAWL_WARC = CorpusFixtures.CRAWL_WARC;

    /// <summary>SHA-256 over the extracted text of every HTML response of the generated WARC shard, in file order: pages in sixteen charsets, with their elements closed and left open.</summary>
    private const string EXTRACTED_TEXT_SHA256 = "2a5293c7e9f760b18e8fc007a575e4fe7f6016c37a5365ac5eb5be93e00033ec";

    /// <summary>The same over the head of the real WARC file.</summary>
    private const string CRAWL_EXTRACTED_TEXT_SHA256 = "1ccba0aeb54b9c6ad18b923a6c53bfe4ceb98781d6f7a51428e2bd28fc4b8d90";

    #endregion

    #region Rule Tests

    [Test]
    public void TitleFirstBlocksAsLinesScriptsAndNavigationDroppedTest()
    {
        const string html = """
            <html><head><title> A &amp; B </title><script>var x = 1;</script><style>p{}</style></head>
            <body><nav><a href="/">Home</a></nav>
            <h1>Heading</h1><p>First   paragraph<br>second line</p>
            <div hidden>secret</div><div style="display:none">also secret</div>
            <ul><li>one</li><li>two <b>bold</b> words</li></ul>
            <footer>© 2026</footer><form><input value="x"><button>Go</button></form></body></html>
            """;

        var text = HtmlTextExtractor.Extract(html);

        Assert.That(text, Is.EqualTo("A & B\nHeading\nFirst paragraph\nsecond line\none\ntwo bold words"));
    }

    [Test]
    public void EntitiesAndWhitespaceAreNormalizedTest()
    {
        var text = HtmlTextExtractor.Extract("<p>caf&eacute;&nbsp;&nbsp;au\n\n\tlait &lt;ok&gt;</p>");

        Assert.That(text, Is.EqualTo("café au lait <ok>"));
    }

    [Test]
    public void HostileNestingIsRefusedBeforeParsingTest()
    {
        // The HTML parsing algorithm walks its open-element stack per tag: 50,000 nested
        // elements took 31 s to parse. The pre-scan refuses them in milliseconds.
        var nested = new StringBuilder("<html><body>");
        for (var i = 0; i < 50_000; i++)
            nested.Append("<div><span>");
        nested.Append("deep");

        var stopwatch = Stopwatch.StartNew();
        var extracted = HtmlTextExtractor.TryExtract(nested.ToString(), out var text, out var refusal);
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(extracted, Is.False);
            Assert.That(text, Is.Empty);
            Assert.That(refusal, Does.Contain("nested deeper than"));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void TooManyTagsAreRefusedAndLegitimateDepthIsNotTest()
    {
        var flat = new StringBuilder("<html><body>");
        for (var i = 0; i < HtmlTextExtractor.MAX_TAGS; i++)
            flat.Append("<p>x</p>");
        var deepButLegit = new StringBuilder("<html><body>");
        for (var i = 0; i < 200; i++)
            deepButLegit.Append("<div>");
        deepButLegit.Append("<p>text <img src=x><br>more<!-- <div><div><div> --><script>var a = '<div>'.repeat(9999);</script></p>");

        Assert.Multiple(() =>
        {
            Assert.That(HtmlTextExtractor.TryExtract(flat.ToString(), out _, out var tagsRefusal), Is.False);
            Assert.That(tagsRefusal, Does.Contain("tags"));
            Assert.That(HtmlTextExtractor.TryExtract(deepButLegit.ToString(), out var text, out var refusal), Is.True, refusal);
            Assert.That(text, Is.EqualTo("text\nmore"));
        });
    }

    [Test]
    public void BrokenMarkupStillYieldsItsTextTest()
    {
        var text = HtmlTextExtractor.Extract("<html><body><p>unclosed <b>bold <i>italic <table><tr><td>cell</div></html>");

        Assert.That(text, Does.Contain("unclosed bold italic").And.Contain("cell"));
    }

    [Test]
    public void OversizedDocumentIsCutNotRefusedTest()
    {
        var huge = "<p>" + new string('x', HtmlTextExtractor.MAX_HTML_CHARS + 1000) + "</p>";

        var text = HtmlTextExtractor.Extract(huge);

        Assert.That(text, Has.Length.LessThanOrEqualTo(HtmlTextExtractor.MAX_HTML_CHARS));
        Assert.That(text, Does.StartWith("xxxx"));
    }

    #endregion

    #region Shard Tests

    [TestCase(WARC_FIXTURE)]
    [TestCase(CRAWL_WARC, Category = CorpusFixtures.CRAWL)]
    public void EveryHtmlResponseOfTheShardDecodesAndExtractsTest(string fixture)
    {
        var responses = Responses(fixture);
        var html = responses.Where(r => r.Payload.IsHtml).ToList();
        var extracted = html.Select(r => HtmlTextExtractor.TryExtract(r.Payload.DecodeBody(), out var text, out _) ? text : "").ToList();

        TestContext.Out.WriteLine($"{responses.Count} responses, {html.Count} HTML, media types: {string.Join(", ", responses.GroupBy(r => r.Payload.MediaType).Select(g => $"{g.Key}={g.Count()}"))}");
        TestContext.Out.WriteLine($"charsets: {string.Join(", ", html.GroupBy(r => r.Payload.Charset ?? "(none)").Select(g => $"{g.Key}={g.Count()}"))}");

        Assert.Multiple(() =>
        {
            Assert.That(html, Has.Count.GreaterThan(80));
            Assert.That(extracted.Count(t => t.Length > 0), Is.GreaterThan(html.Count * 8 / 10), "most pages carry text");
            Assert.That(extracted.All(t => !t.Contains("<script", StringComparison.OrdinalIgnoreCase)), Is.True);
        });
    }

    [TestCase(WARC_FIXTURE, EXTRACTED_TEXT_SHA256)]
    [TestCase(CRAWL_WARC, CRAWL_EXTRACTED_TEXT_SHA256, Category = CorpusFixtures.CRAWL)]
    public void L7ExtractionIsTheSameBytesOnEveryMachineTest(string fixture, string expected)
    {
        // Audit finding L-7: this test compared an extraction with a second one in the same
        // process, which cannot differ. The text of a shard's pages is pinned instead: the
        // three platforms of the build server are held to one value.
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var response in Responses(fixture).Where(r => r.Payload.IsHtml))
            sha.AppendData(Encoding.UTF8.GetBytes(HtmlTextExtractor.Extract(response.Payload.DecodeBody())));

        Assert.That(Convert.ToHexStringLower(sha.GetHashAndReset()), Is.EqualTo(expected), "the extractor's text of the shard's pages: a changed value is a changed extractor (or, for the generated shard, a changed generator), and the pinned content hashes move with it");
    }

    [TestCase(WARC_FIXTURE, WET_FIXTURE)]
    [TestCase(CRAWL_WARC, CRAWL_WET, Category = CorpusFixtures.CRAWL)]
    public void OurTextAgreesWithCommonCrawlsWetConversionTest(string warc, string wetFixture)
    {
        // The WET shard is the conversion of the same WARC file: same URLs, in order (Common
        // Crawl's own conversion for the real heads; for the generated shards the text the
        // generator gives each page, its menu and its foot included). Token overlap (Jaccard
        // over word sets) is an agreement statistic, not an identity - the two extractors
        // differ by design - but it must sit well above chance.
        var wet = new Dictionary<string, string>();
        using (var reader = WarcReader.Open(Fixture(wetFixture)))
        {
            foreach (var record in reader.ReadRecords().Where(r => r.Type == "conversion"))
                wet.TryAdd(record.TargetUri!, Encoding.UTF8.GetString(record.Payload.Span));
        }

        var scores = new List<double>();
        foreach (var response in Responses(warc).Where(r => r.Payload.IsHtml))
        {
            if (!wet.TryGetValue(response.Uri, out var reference))
                continue;

            var ours = Tokens(HtmlTextExtractor.Extract(response.Payload.DecodeBody()));
            var theirs = Tokens(reference);
            if (ours.Count == 0 && theirs.Count == 0)
                continue;

            scores.Add((double)ours.Intersect(theirs).Count() / ours.Union(theirs).Count());
        }

        scores.Sort();
        var median = scores[scores.Count / 2];
        TestContext.Out.WriteLine($"{scores.Count} pages compared with WET: median Jaccard {median:F2}, lower quartile {scores[scores.Count / 4]:F2}, upper quartile {scores[scores.Count * 3 / 4]:F2}");

        Assert.That(scores, Has.Count.GreaterThan(50));
        Assert.That(median, Is.GreaterThan(0.4));
    }

    #endregion

    #region Helpers

    private static List<(string Uri, HttpResponsePayload Payload)> Responses(string fixture)
    {
        using var reader = WarcReader.Open(Fixture(fixture));
        var responses = new List<(string, HttpResponsePayload)>();
        foreach (var record in reader.ReadRecords().Where(r => r.Type == "response"))
        {
            var payload = HttpResponsePayload.Parse(record.Payload);
            if (payload != null)
                responses.Add((record.TargetUri!, payload));
        }

        return responses;
    }

    private static HashSet<string> Tokens(string text)
    {
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']').ToLowerInvariant())
            .Where(t => t.Length > 1)
            .ToHashSet();
    }

    private static string Fixture(string name)
    {
        return CorpusFixtures.PathOf(name);
    }

    #endregion
}
