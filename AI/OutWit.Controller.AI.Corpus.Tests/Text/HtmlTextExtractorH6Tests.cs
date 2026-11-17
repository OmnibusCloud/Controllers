using System.Diagnostics;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Text;

/// <summary>
/// Audit finding H-6: the pre-scan that guards the HTML parser and the parser disagreed on where
/// a comment and a text element end, so five characters hid any nesting from the caps and one
/// page held a core for minutes. The pre-scan now reads those as a tokenizer does, and behind it
/// the parser itself is held to a budget on its own tree, which no reading of the markup can
/// argue with: a page past it drops as <c>html_too_complex</c>.
/// </summary>
[TestFixture]
public sealed class HtmlTextExtractorH6Tests
{
    #region Constants

    /// <summary>Generous for a loaded CI machine; the pages below took minutes, or grew without bound, before.</summary>
    private static readonly TimeSpan SOON = TimeSpan.FromSeconds(5);

    private const int DEEP = 40_000;

    #endregion

    #region Pre-scan Tests

    [TestCase("<!-->")]
    [TestCase("<!--->")]
    [TestCase("<!-- a comment --!>")]
    [TestCase("<xmp><script></xmp>")]
    [TestCase("<XMP><script></XmP >")]
    [TestCase("<title><script></title>")]
    [TestCase("<textarea><style></textarea>")]
    [TestCase("<iframe><script></iframe>")]
    [TestCase("<noembed><script></noembed>")]
    [TestCase("<noframes><style></noframes>")]
    public void H6WhatHidNestingFromThePreScanNoLongerDoesTest(string prefix)
    {
        // The audit's page: the prefix made the pre-scan skip everything after it (to a "-->" or a
        // "</script" that the parser never looks for), and the parser nested 800,000 levels.
        var html = "<html><body>" + prefix + Repeat("<div>", DEEP) + "text-->";

        var clock = Stopwatch.StartNew();
        var reading = HtmlTextExtractor.Read(html, out var text);

        Assert.Multiple(() =>
        {
            Assert.That(HtmlTextExtractor.Inspect(html), Is.EqualTo("elements nested deeper than 512"));
            Assert.That(reading, Is.EqualTo(HtmlReading.Refused));
            Assert.That(text, Is.Empty);
            Assert.That(clock.Elapsed, Is.LessThan(SOON), "40,000 levels took the parser 3.7 s, 800,000 about 25 minutes");
        });
    }

    [Test]
    public void H6AfterPlaintextNothingIsMarkupTest()
    {
        var html = "<html><body><p>before</p><plaintext>" + Repeat("<div>", DEEP);

        Assert.Multiple(() =>
        {
            Assert.That(HtmlTextExtractor.Inspect(html), Is.Null, "the parser reads the rest as text, and so does the pre-scan");
            Assert.That(HtmlTextExtractor.Read(html, out var text), Is.EqualTo(HtmlReading.Text));
            Assert.That(text, Does.StartWith("before\n<div><div>"));
        });
    }

    [Test]
    public void H6AnHonestPageWithTheseElementsStillReadsTest()
    {
        var html = "<html><head><title>A title</title><style>p { color: red }</style><script>if (a < b) { c(); }</script></head>" +
                   "<body><!-- a comment --><!----><p>First paragraph.</p><textarea><b>not markup</b></textarea><xmp><i>shown as written</i></xmp><p>Second paragraph.</p></body></html>";

        Assert.Multiple(() =>
        {
            Assert.That(HtmlTextExtractor.Inspect(html), Is.Null);
            Assert.That(HtmlTextExtractor.Read(html, out var text), Is.EqualTo(HtmlReading.Text));
            Assert.That(text, Is.EqualTo("A title\nFirst paragraph.\n<i>shown as written</i>\nSecond paragraph."));
        });
    }

    #endregion

    #region Parser Budget Tests

    [TestCase("unmatched end tags", "<div></x>", 29_000)]
    [TestCase("an open tag behind a less-than sign", "<3<div>", 20_000)]
    [TestCase("inside svg style", null, 0)]
    [TestCase("inside svg script", null, 1)]
    public void H6APageThePreScanCannotSeeThroughIsStoppedByTheParsersOwnBudgetTest(string name, string? unit, int count)
    {
        // Markup the pre-scan reads as shallow and the parser nests without end: an end tag that
        // closes nothing, a tag the pre-scan takes for the rest of "<3 ...>", an HTML element
        // inside an SVG style or script (text to a tokenizer in HTML, markup in SVG). The
        // parser's tree reaches the nesting limit and the parse stops.
        var html = unit != null
            ? "<html><body>" + Repeat(unit, count) + "text"
            : "<html><body><svg>" + (count == 0 ? "<style>" : "<script>") + Repeat("<div>", DEEP) + "text";

        var clock = Stopwatch.StartNew();
        var reading = HtmlTextExtractor.Read(html, out var text);

        Assert.Multiple(() =>
        {
            Assert.That(HtmlTextExtractor.Inspect(html), Is.Null, "the pre-scan lets it through");
            Assert.That(reading, Is.EqualTo(HtmlReading.TooComplex), name);
            Assert.That(text, Is.Empty);
            Assert.That(clock.Elapsed, Is.LessThan(SOON));
        });
    }

    [TestCase("a paragraph after each", "<p>x</p>")]
    [TestCase("a line break first", "<p><br>x</p>")]
    [TestCase("a block that closes the paragraph", "<div>x</div>")]
    public void H6FormattingElementsOpenedAgainInEveryBlockAreCountedTest(string name, string block)
    {
        // Four hundred unclosed <b> told apart by an attribute, then blocks: the parser opens all
        // four hundred again in each, without a tag and without telling. Some 40 KB of markup
        // are two million elements; sixty thousand tags would be twelve million.
        var html = "<html><body><p>" + string.Concat(Enumerable.Range(0, 400).Select(index => $"<b a={index}>")) + Repeat(block, 5_000);

        var clock = Stopwatch.StartNew();
        var reading = HtmlTextExtractor.Read(html, out _);

        Assert.Multiple(() =>
        {
            Assert.That(html.Length, Is.LessThan(100_000));
            Assert.That(HtmlTextExtractor.Inspect(html), Is.Null, "401 levels and 5,400 tags: within the pre-scan's caps");
            Assert.That(reading, Is.EqualTo(HtmlReading.TooComplex), name);
            Assert.That(clock.Elapsed, Is.LessThan(SOON));
        });
    }

    [Test]
    public void H6LargeHonestPagesAreWithinTheBudgetTest()
    {
        (string Name, string Html, int Lines)[] pages =
        [
            ("thirty thousand paragraphs", "<html><body>" + Repeat("<p>text</p>", 29_990), 29_990),
            ("a list of thirty thousand items", "<html><body><ul>" + Repeat("<li>item</li>", 29_990) + "</ul>", 29_990),
            ("a table of twenty-nine thousand cells", "<html><body><table>" + Repeat("<tr>" + Repeat("<td>c</td>", 100) + "</tr>", 290) + "</table>", 29_000),
            ("twenty-nine thousand spans in one paragraph", "<html><body><p>" + Repeat("<span>a</span> ", 29_000), 1),
            ("five hundred levels", "<html><body>" + Repeat("<div>", 500) + "deep" + Repeat("</div>", 500), 1),
            ("an unclosed bold in a paragraph, two thousand paragraphs after it", "<html><body><p><b>" + Repeat("<p>bold text</p>", 2_000), 2_000)
        ];

        Assert.Multiple(() =>
        {
            foreach (var (name, html, lines) in pages)
            {
                var clock = Stopwatch.StartNew();
                var reading = HtmlTextExtractor.Read(html, out var text);

                Assert.That(reading, Is.EqualTo(HtmlReading.Text), name);
                Assert.That(text.Split('\n'), Has.Length.EqualTo(lines), name);
                Assert.That(clock.Elapsed, Is.LessThan(SOON), name);
            }
        });
    }

    [TestCase("a<!--c-->", 150_000)]
    [TestCase("<!--c--!>a", 150_000)]
    [TestCase("<!-- - -- - ", 150_000)]
    [TestCase("<title>t</title>", 3_500)]
    public void H6ThePreScanReadsAPageOnceTest(string unit, int count)
    {
        // A page of comments: looking for each comment's end twice, to the end of the page when
        // one of the two endings never comes, cost the square of the page (15 s for 2.7 MB of them).
        var html = "<html><body><p>" + Repeat(unit, count);

        var clock = Stopwatch.StartNew();
        var refusal = HtmlTextExtractor.Inspect(html);
        var inspected = clock.Elapsed;
        var reading = HtmlTextExtractor.Read(html, out _);

        Assert.Multiple(() =>
        {
            Assert.That(html.Length, Is.LessThan(HtmlTextExtractor.MAX_HTML_CHARS));
            Assert.That(refusal, Is.Null);
            Assert.That(reading, Is.EqualTo(HtmlReading.Text));
            Assert.That(inspected, Is.LessThan(TimeSpan.FromSeconds(1)), "the pre-scan is one pass");
            Assert.That(clock.Elapsed, Is.LessThan(SOON));
        });
    }

    [Test]
    public void H6APagePastTheBudgetIsADropOfThatPageTest()
    {
        var good = WarcSynth.HtmlResponseRecord("https://a.example/good", "<html><body><p>" + WarcSynth.Paragraphs(new Random(7), 400) + "</p></body></html>");
        var hostile = WarcSynth.HtmlResponseRecord("https://a.example/hostile", "<html><body>" + Repeat("<div></x>", 29_000) + "text");
        var refused = WarcSynth.HtmlResponseRecord("https://a.example/refused", "<html><body><!-->" + Repeat("<div>", DEEP));
        var pipeline = new CorpusPipelineData { Version = "h6", IdentifyLanguage = false, ExtractPdf = false, CleanLines = false, FilterQuality = false };

        var product = new ShardProcessor(pipeline, null).Process(new MemoryStream(WarcSynth.Shard([good, hostile, refused, good])), CorpusSourceFormat.Warc, new MemoryStream(), new MemoryStream());
        var stats = product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(stats.DocumentsOut, Is.EqualTo(2));
            Assert.That(stats.Drops.Select(drop => (drop.Name, drop.Count)), Is.EqualTo(new[] { ("html_refused", 1), ("html_too_complex", 1) }));
            Assert.That(ShardDropRules.StageOf("html_too_complex"), Is.EqualTo(ShardDropRules.STAGE_EXTRACT));
        });
    }

    [Test]
    public void H6TheBudgetIsTheSameEveryTimeTest()
    {
        // The drop is a function of the page: the same page, the same answer, on a fresh thread
        // (a fresh parser) as on a warm one.
        var html = "<html><body><p>" + string.Concat(Enumerable.Range(0, 300).Select(index => $"<b a={index}>")) + Repeat("<p>x</p>", 390);
        var over = "<html><body><p>" + string.Concat(Enumerable.Range(0, 300).Select(index => $"<b a={index}>")) + Repeat("<p>x</p>", 410);

        var answers = new List<(HtmlReading Within, HtmlReading Over)>();
        for (var round = 0; round < 3; round++)
        {
            var thread = new Thread(() => answers.Add((HtmlTextExtractor.Read(html, out _), HtmlTextExtractor.Read(over, out _))));
            thread.Start();
            thread.Join();
        }

        Assert.That(answers, Is.All.EqualTo((HtmlReading.Text, HtmlReading.TooComplex)), "390 blocks of 300 copies are within 120,000 elements, 410 are not");
    }

    #endregion

    #region Tools

    private static string Repeat(string unit, int count)
    {
        return string.Concat(Enumerable.Repeat(unit, count));
    }

    #endregion
}
