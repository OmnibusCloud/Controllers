using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Pdf;

/// <summary>
/// The extractor in Corpus's sandbox on its contract set (PDFs of our own making): pages in the
/// page tree's order, an empty page, the hyphen rule, control characters turned into line ends,
/// RC4 with the empty password read and with another refused, no pages, not a PDF, an empty
/// file. Then the envelope: a run under too little fuel is a fuel verdict that reports its whole
/// budget, too much text is oversized, the same PDF twice is the same text and the same fuel,
/// and the products are pinned — text hash and fuel, the same on every platform the workflow
/// runs (the fuel is the guest's alone: the budget is given after instantiation). Needs the
/// module (<see cref="PdfTestModule"/>).
/// </summary>
[TestFixture]
public sealed class PdfSandboxTests
{
    #region Constants

    /// <summary>
    /// The contract set through the stage's sandbox: verdict, pages, text SHA-256 (of the module's
    /// UTF-8 text, "" for none) and fuel. Produced on win-x64 and re-checked on every platform
    /// the workflow runs; a new module or engine moves them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (PdfVerdict Verdict, int Pages, string Sha256, long Fuel)> GOLDEN = new Dictionary<string, (PdfVerdict, int, string, long)>
    {
        ["control-characters.pdf"] = (PdfVerdict.Text, 1, "27db3473a6c5b34d6be267fa64fdbf4145ba7a0ebb483dcf31a6c4d7cb370f19", 3028117),
        ["empty-file.pdf"] = (PdfVerdict.Unreadable, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 20015),
        ["empty-page.pdf"] = (PdfVerdict.Text, 3, "227a0d2c04c227045b5c52c0d970eaa4edc2f255e5956211ba0243f8c7be91e3", 3986025),
        ["encrypted-empty-password.pdf"] = (PdfVerdict.Text, 1, "f20a25338614fea749f44e2c6910397423f655a8589bffe8b1483dbdcc0cdcd9", 3670784),
        ["encrypted-user-password.pdf"] = (PdfVerdict.Encrypted, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 943758),
        ["hyphenated-line.pdf"] = (PdfVerdict.Text, 1, "10f6e89ae48940158dc6836f9c69667c2678b57f443637fdcfc49b36cf117f3d", 3604153),
        ["no-pages.pdf"] = (PdfVerdict.NoPages, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 479617),
        ["not-a-pdf.pdf"] = (PdfVerdict.Unreadable, 0, "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", 1540540),
        ["one-page.pdf"] = (PdfVerdict.Text, 1, "31015e014c7070fd1c3905e854bb7d50f6b5cecbcf6357cc18fbbf38d284bc47", 3027754),
        ["three-pages-nested-tree.pdf"] = (PdfVerdict.Text, 3, "265f132dc5eceff6f4ea60432fe9cb4b11e6dda7b07eb9c1953ce85df31c6769", 4362140),
        ["three-pages.pdf"] = (PdfVerdict.Text, 3, "265f132dc5eceff6f4ea60432fe9cb4b11e6dda7b07eb9c1953ce85df31c6769", 4319443)
    };

    #endregion

    #region Fields

    private PdfSandbox m_sandbox = null!;
    private string m_work = null!;
    private Dictionary<string, PdfExtraction> m_results = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_sandbox = new PdfSandbox(PdfTestModule.Require());
        m_work = Path.Combine(Path.GetTempPath(), $"witai_corpus_pdf_{Guid.NewGuid():N}");
        m_results = PdfSynth.Contract().ToDictionary(file => file.Name, file => m_sandbox.Extract(file.Bytes, m_work));

        foreach (var (name, result) in m_results.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            TestContext.Progress.WriteLine(Golden(name, result));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_sandbox?.Dispose();
        if (m_work != null && Directory.Exists(m_work))
            Directory.Delete(m_work, recursive: true);
    }

    #endregion

    #region Contract Tests

    [TestCase("one-page.pdf")]
    [TestCase("three-pages.pdf")]
    [TestCase("three-pages-nested-tree.pdf")]
    public void PagesComeInTheTreesOrderTest(string name)
    {
        var expected = name == "one-page.pdf" ? new[] { "The only page of a one-page document." } : PdfSynth.THREE_PAGES.ToArray();
        var result = m_results[name];

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(PdfVerdict.Text));
            Assert.That(result.Pages, Is.EqualTo(expected.Length));
            Assert.That(result.Encrypted, Is.False);
            Assert.That(PageTexts(result), Is.EqualTo(expected));
        });
    }

    [Test]
    public void AnEmptyPageIsAPageTest()
    {
        Assert.That(PageTexts(m_results["empty-page.pdf"]), Is.EqualTo(new[] { PdfSynth.THREE_PAGES[0], "", PdfSynth.THREE_PAGES[2] }));
    }

    [Test]
    public void TheExtractorRejoinsABrokenWordAndKeepsALabelsHyphenTest()
    {
        Assert.That(PageTexts(m_results["hyphenated-line.pdf"]), Is.EqualTo(new[] { PdfSynth.HYPHEN_TEXT }));
    }

    [Test]
    public void ControlCharactersBecomeLineEndsTest()
    {
        var text = m_results["control-characters.pdf"].Text;

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("carriage").And.Contain("return").And.Contain("form").And.Contain("feed"));
            Assert.That(text, Does.Not.Contain("\r").And.Not.Contain("\f"), "one page: no form feed, and no carriage return at all");
        });
    }

    [Test]
    public void TheEmptyPasswordIsReadAndAnotherIsRefusedTest()
    {
        var empty = m_results["encrypted-empty-password.pdf"];
        var other = m_results["encrypted-user-password.pdf"];

        Assert.Multiple(() =>
        {
            Assert.That(empty.Verdict, Is.EqualTo(PdfVerdict.Text));
            Assert.That(empty.Encrypted, Is.True);
            Assert.That(PageTexts(empty), Is.EqualTo(new[] { PdfSynth.EMPTY_PASSWORD_TEXT }));
            Assert.That(other.Verdict, Is.EqualTo(PdfVerdict.Encrypted));
            Assert.That(other.Text, Is.Empty);
        });
    }

    [TestCase("no-pages.pdf", PdfVerdict.NoPages)]
    [TestCase("not-a-pdf.pdf", PdfVerdict.Unreadable)]
    [TestCase("empty-file.pdf", PdfVerdict.Unreadable)]
    public void ARefusedDocumentHasItsVerdictAndNoTextTest(string name, PdfVerdict verdict)
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_results[name].Verdict, Is.EqualTo(verdict));
            Assert.That(m_results[name].Text, Is.Empty);
        });
    }

    #endregion

    #region Envelope Tests

    [Test]
    public void TooLittleFuelIsAFuelVerdictWithItsWholeBudgetTest()
    {
        using var starved = new PdfSandbox(PdfTestModule.Require(), 100_000, PdfStageLimits.MEMORY_BYTES);

        var result = starved.Extract(PdfSynth.Document([["Not enough fuel to read this page."]]), m_work);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(PdfVerdict.Fuel));
            Assert.That(result.Fuel, Is.EqualTo(100_000));
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void MoreTextThanTheCapIsOversizedTest()
    {
        using var capped = new PdfSandbox(PdfTestModule.Require(), PdfStageLimits.FUEL, PdfStageLimits.MEMORY_BYTES, maxTextBytes: 16);

        var result = capped.Extract(PdfSynth.Document([["This one line is longer than sixteen bytes."]]), m_work);

        Assert.That(result.Verdict, Is.EqualTo(PdfVerdict.Oversized));
    }

    [Test]
    public void AnInputPastTheCapIsOversizedWithoutARunTest()
    {
        var result = m_sandbox.Extract(new byte[PdfStageLimits.MAX_INPUT_BYTES + 1], m_work);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(PdfVerdict.Oversized));
            Assert.That(result.Fuel, Is.Zero);
        });
    }

    [Test]
    public void TheSamePdfTwiceIsTheSameTextAndTheSameFuelTest()
    {
        var bytes = PdfSynth.Contract().Single(file => file.Name == "three-pages.pdf").Bytes;

        var again = m_sandbox.Extract(bytes, m_work);
        var first = m_results["three-pages.pdf"];

        Assert.Multiple(() =>
        {
            Assert.That(again.Text, Is.EqualTo(first.Text));
            Assert.That(again.Fuel, Is.EqualTo(first.Fuel));
            Assert.That(again.Fuel, Is.GreaterThan(0).And.LessThan(PdfStageLimits.FUEL));
        });
    }

    [Test]
    public void TheRunLeavesNothingInItsFoldersTest()
    {
        var work = Path.Combine(m_work, "leaves-nothing");
        m_sandbox.Extract(PdfSynth.Document([["A page."]]), work);

        Assert.That(Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void ConcurrentRunsGiveTheSingleRunsTextTest()
    {
        var bytes = PdfSynth.Contract().Single(file => file.Name == "three-pages-nested-tree.pdf").Bytes;
        var expected = m_results["three-pages-nested-tree.pdf"];
        var results = new PdfExtraction[8];

        Parallel.For(0, results.Length, index => results[index] = m_sandbox.Extract(bytes, Path.Combine(m_work, $"concurrent-{index}")));

        Assert.That(results.Select(result => (result.Text, result.Fuel)), Is.All.EqualTo((expected.Text, expected.Fuel)));
    }

    #endregion

    #region Golden Tests

    [Test]
    public void TheContractSetsProductsArePinnedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_results.Keys, Is.EquivalentTo(GOLDEN.Keys), "every case is pinned");
            foreach (var (name, result) in m_results)
            {
                if (!GOLDEN.TryGetValue(name, out var golden))
                    continue;

                Assert.That((result.Verdict, result.Pages, Sha256(result.Text), result.Fuel), Is.EqualTo(golden), Golden(name, result));
            }
        });
    }

    #endregion

    #region Tools

    /// <summary>The text of each page, trimmed.</summary>
    public static IReadOnlyList<string> PageTexts(PdfExtraction result)
    {
        return result.Text.Split(PdfTextShaper.PAGE_BREAK).Select(page => page.Trim()).ToList();
    }

    /// <summary>Lower-case hex SHA-256 of a text's UTF-8 bytes.</summary>
    public static string Sha256(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>A result as a line of a golden table.</summary>
    public static string Golden(string name, PdfExtraction result)
    {
        return string.Create(CultureInfo.InvariantCulture, $"        [\"{name}\"] = (PdfVerdict.{result.Verdict}, {result.Pages}, \"{Sha256(result.Text)}\", {result.Fuel}),");
    }

    #endregion
}
