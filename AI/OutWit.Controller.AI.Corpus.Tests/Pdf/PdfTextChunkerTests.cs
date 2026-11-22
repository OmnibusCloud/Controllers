using OutWit.Controller.AI.Corpus.Pdf;

namespace OutWit.Controller.AI.Corpus.Tests.Pdf;

/// <summary>
/// The chunk rule: pages are put together into documents of at most the cap while they fit, a
/// page that does not fit starts the next one, a page longer than the cap is a document of its
/// own cut after its last whole line that fits (a single line too long, after its last word), and
/// the ranges name the pages — empty pages inside a range belong to it, empty pages around it do
/// not. Words are counted as the quality filter counts a mixed text.
/// </summary>
[TestFixture]
public sealed class PdfTextChunkerTests
{
    #region Word Tests

    [TestCase("", 0)]
    [TestCase("one two three", 3)]
    [TestCase("  spaced   out  ", 2)]
    [TestCase("don't stop-and-go", 2)]
    [TestCase("— – ...", 0)]
    [TestCase("2019 1,000", 2)]
    [TestCase("日本語テキスト", 7)]
    [TestCase("iPhone発売", 3)]
    [TestCase("日本。", 2)]
    public void WordsAreCountedAsTheQualityFilterCountsThemTest(string line, int words)
    {
        Assert.That(PdfTextChunker.Words(line), Is.EqualTo(words));
    }

    [Test]
    public void APrefixEndsBeforeTheWordPastTheLimitTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PdfTextChunker.Prefix("one two three four", 2), Is.EqualTo("one two"));
            Assert.That(PdfTextChunker.Prefix("one two", 5), Is.EqualTo("one two"));
            Assert.That(PdfTextChunker.Prefix("日本語テキスト", 3), Is.EqualTo("日本語"));
            Assert.That(PdfTextChunker.Prefix("a, b, c", 2), Is.EqualTo("a, b,"));
        });
    }

    #endregion

    #region Chunk Tests

    [Test]
    public void PagesGoTogetherWhileTheyFitTest()
    {
        var pages = Pages(4, 4, 4, 4, 4);
        var (chunks, cut) = PdfTextChunker.Chunk(pages, maxWords: 10);

        Assert.Multiple(() =>
        {
            Assert.That(chunks.Select(chunk => (chunk.FirstPage, chunk.LastPage)), Is.EqualTo(new[] { (1, 2), (3, 4), (5, 5) }));
            Assert.That(chunks[0].Text, Is.EqualTo("p1w1 p1w2 p1w3 p1w4\np2w1 p2w2 p2w3 p2w4"));
            Assert.That(cut, Is.Zero);
        });
    }

    [Test]
    public void APageThatFillsTheCapExactlyStaysWholeTest()
    {
        var (chunks, cut) = PdfTextChunker.Chunk(Pages(10, 3), maxWords: 10);

        Assert.Multiple(() =>
        {
            Assert.That(chunks.Select(chunk => (chunk.FirstPage, chunk.LastPage)), Is.EqualTo(new[] { (1, 1), (2, 2) }));
            Assert.That(cut, Is.Zero);
        });
    }

    [Test]
    public void APageLongerThanTheCapIsItsOwnDocumentCutAtALineTest()
    {
        // Page 2 has three lines of four words: twelve words over a cap of ten keep two lines.
        List<string[]> pages =
        [
            ["a b c"],
            ["l1 l1 l1 l1", "l2 l2 l2 l2", "l3 l3 l3 l3"],
            ["d e f"]
        ];

        var (chunks, cut) = PdfTextChunker.Chunk(pages, maxWords: 10);

        Assert.Multiple(() =>
        {
            Assert.That(chunks.Select(chunk => (chunk.FirstPage, chunk.LastPage)), Is.EqualTo(new[] { (1, 1), (2, 2), (3, 3) }));
            Assert.That(chunks[1].Text, Is.EqualTo("l1 l1 l1 l1\nl2 l2 l2 l2"));
            Assert.That(cut, Is.EqualTo(1));
        });
    }

    [Test]
    public void ASingleLineLongerThanTheCapIsCutAfterAWordTest()
    {
        List<string[]> pages = [[string.Join(' ', Enumerable.Range(1, 25).Select(index => $"w{index}"))]];

        var (chunks, cut) = PdfTextChunker.Chunk(pages, maxWords: 10);

        Assert.Multiple(() =>
        {
            Assert.That(chunks.Single().Text, Is.EqualTo(string.Join(' ', Enumerable.Range(1, 10).Select(index => $"w{index}"))));
            Assert.That(PdfTextChunker.Words(chunks.Single().Text), Is.EqualTo(10));
            Assert.That(cut, Is.EqualTo(1));
        });
    }

    [Test]
    public void EmptyPagesBelongToTheRangeTheyAreInsideTest()
    {
        List<string[]> pages = [[], ["a b"], [], ["c d"], [], []];

        var (chunks, _) = PdfTextChunker.Chunk(pages, maxWords: 10);

        Assert.Multiple(() =>
        {
            Assert.That(chunks.Single().FirstPage, Is.EqualTo(2));
            Assert.That(chunks.Single().LastPage, Is.EqualTo(4));
            Assert.That(chunks.Single().Text, Is.EqualTo("a b\nc d"));
        });
    }

    [Test]
    public void APdfWithoutTextHasNoDocumentTest()
    {
        List<string[]> pages = [[], []];
        var (chunks, cut) = PdfTextChunker.Chunk(pages);

        Assert.Multiple(() =>
        {
            Assert.That(chunks, Is.Empty);
            Assert.That(cut, Is.Zero);
        });
    }

    [Test]
    public void EveryDocumentStaysWithinTheCapTest()
    {
        var random = new Random(7);
        var pages = Enumerable.Range(0, 40).Select(_ => Enumerable.Range(0, random.Next(0, 30)).Select(line => string.Join(' ', Enumerable.Repeat("w", random.Next(1, 400)))).ToArray()).ToList();

        var (chunks, _) = PdfTextChunker.Chunk(pages, maxWords: 1000);

        Assert.Multiple(() =>
        {
            Assert.That(chunks.Select(chunk => PdfTextChunker.Words(chunk.Text.Replace('\n', ' '))), Is.All.LessThanOrEqualTo(1000));
            Assert.That(chunks.Zip(chunks.Skip(1)).All(pair => pair.First.LastPage < pair.Second.FirstPage), Is.True, "the ranges are in order and do not overlap");
        });
    }

    [Test]
    public void TheIdNamesTheRangeAndReplacesAFragmentTest()
    {
        var chunk = new PdfTextChunk(3, 7, "text");

        Assert.Multiple(() =>
        {
            Assert.That(chunk.Id("https://example.org/a.pdf"), Is.EqualTo("https://example.org/a.pdf#page=3-7"));
            Assert.That(chunk.Id("https://example.org/a.pdf#section"), Is.EqualTo("https://example.org/a.pdf#page=3-7"));
        });
    }

    #endregion

    #region Tools

    private static List<string[]> Pages(params int[] words)
    {
        return words.Select((count, index) => new[] { string.Join(' ', Enumerable.Range(1, count).Select(word => $"p{index + 1}w{word}")) }).ToList();
    }

    #endregion
}
