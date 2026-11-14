using OutWit.Controller.AI.Corpus.Pdf;

namespace OutWit.Controller.AI.Corpus.Tests.Pdf;

/// <summary>
/// The shaping of the extractor's text: pages at U+000C, whitespace collapsed the way a WET or
/// HTML document's is, and running headers and footers — a line on half the pages or more, digits
/// folded — gone from every page of a PDF with enough pages, and only those.
/// </summary>
[TestFixture]
public sealed class PdfTextShaperTests
{
    #region Pages Tests

    [Test]
    public void PagesSplitAtFormFeedsTest()
    {
        var shaped = PdfTextShaper.Shape("first page\fsecond page\f\fFourth page");

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Pages, Has.Count.EqualTo(4));
            Assert.That(shaped.Pages[0], Is.EqualTo(new[] { "first page" }));
            Assert.That(shaped.Pages[2], Is.Empty, "an empty page is a page without lines");
            Assert.That(shaped.Pages[3], Is.EqualTo(new[] { "Fourth page" }));
            Assert.That(shaped.RunningLinesRemoved, Is.Zero);
        });
    }

    [Test]
    public void WhitespaceCollapsesAndBlankLinesGoTest()
    {
        var shaped = PdfTextShaper.Shape("  two   spaces and a\ttab  \n\n   \nnext line ");

        Assert.That(shaped.Pages[0], Is.EqualTo(new[] { "two spaces and a tab", "next line" }));
    }

    [Test]
    public void AnEmptyTextIsOneEmptyPageTest()
    {
        var shaped = PdfTextShaper.Shape("");

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Pages, Has.Count.EqualTo(1));
            Assert.That(shaped.Pages[0], Is.Empty);
        });
    }

    #endregion

    #region Running Line Tests

    [Test]
    public void ARunningHeaderAndAPageNumberLeaveEveryPageTest()
    {
        string[] names = ["one", "two", "three", "four", "five", "six"];
        var pages = names.Select((name, index) => $"The Annual Report 2025\nThe body of page {name} has words of its own.\n{index + 1}");
        var shaped = PdfTextShaper.Shape(string.Join('\f', pages));

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Pages.Select(page => page.Single()), Is.EqualTo(names.Select(name => $"The body of page {name} has words of its own.")));
            Assert.That(shaped.RunningLinesRemoved, Is.EqualTo(12), "the header and the folded page number, on each of six pages");
        });
    }

    [Test]
    public void PageNumbersOfAnyWidthFoldToOneLineTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PdfTextShaper.Fold("Page 3 of 120"), Is.EqualTo(PdfTextShaper.Fold("Page 117 of 120")));
            Assert.That(PdfTextShaper.Fold("- 12 -"), Is.EqualTo("- 0 -"));
            Assert.That(PdfTextShaper.Fold("Seite ٣"), Is.EqualTo("Seite 0"), "a decimal digit of any script folds");
            Assert.That(PdfTextShaper.Fold("no digits"), Is.EqualTo("no digits"));
        });
    }

    [Test]
    public void ALineOnHalfThePagesIsRunningAndOneOnFewerIsNotTest()
    {
        // Six pages: a line on three of them (half) goes, a line on two of them stays.
        var pages = new[]
        {
            "Chapter One\nA line seen twice.\nUnique one.",
            "Chapter One\nA line seen twice.\nUnique two.",
            "Chapter One\nUnique three.",
            "Unique four.",
            "Unique five.",
            "Unique six."
        };

        var shaped = PdfTextShaper.Shape(string.Join('\f', pages));

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Pages.SelectMany(page => page), Has.None.EqualTo("Chapter One"));
            Assert.That(shaped.Pages.SelectMany(page => page).Count(line => line == "A line seen twice."), Is.EqualTo(2));
            Assert.That(shaped.RunningLinesRemoved, Is.EqualTo(3));
        });
    }

    [Test]
    public void AShortDocumentKeepsItsRepeatedLinesTest()
    {
        // Four pages are fewer than the rule needs: a line on every page stays.
        var pages = Enumerable.Range(1, PdfStageLimits.RUNNING_LINE_MIN_PAGES - 1).Select(page => $"Header\nBody {page}.");
        var shaped = PdfTextShaper.Shape(string.Join('\f', pages));

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Pages.All(page => page[0] == "Header"), Is.True);
            Assert.That(shaped.RunningLinesRemoved, Is.Zero);
        });
    }

    [Test]
    public void ALineRepeatedOnOnePageCountsOnceTest()
    {
        // The same line five times on one page of six is on one page, not five.
        var pages = new[] { "Repeat\nRepeat\nRepeat\nRepeat\nRepeat", "a", "b", "c", "d", "e" };
        var shaped = PdfTextShaper.Shape(string.Join('\f', pages));

        Assert.That(shaped.Pages[0], Has.Length.EqualTo(5));
    }

    #endregion
}
