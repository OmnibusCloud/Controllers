using System.Text;
using OutWit.Controller.AI.Corpus.Pdf;

namespace OutWit.Controller.AI.Corpus.Tests.Pdf;

/// <summary>
/// The hyphen rule beyond Latin: a Cyrillic or Greek word broken at a line end (a hyphen right after
/// a lower-case letter, the next line starting with a lower-case letter of the same script) becomes
/// one word on one line; Latin is left to pdf_oxide, and a break between two scripts, before an
/// upper-case letter, after a digit, or at a page's last line stays as it is.
/// </summary>
[TestFixture]
public sealed class PdfTextHyphensTests
{
    #region Rule Tests

    [TestCase("противниками. В сегодняшней Молдове сто-", "ронники присоединения", true, TestName = "CyrillicJoinsTest")]
    [TestCase("η ιστορία της κοιλά-", "δας και των αγροτών", true, TestName = "GreekJoinsTest")]
    [TestCase("ἀρχὴ τῆς ἱστορὶ-", "ἀς ἐστίν", true, TestName = "GreekExtendedJoinsTest")]
    [TestCase("кириллица ԁо-", "ԁа", true, TestName = "CyrillicSupplementJoinsTest")]
    [TestCase("слово сло‐", "во", true, TestName = "UnicodeHyphenJoinsTest")]
    [TestCase("слово сло­", "во", true, TestName = "SoftHyphenJoinsTest")]
    [TestCase("the words on this line hyphen-", "ated", false, TestName = "LatinIsPdfOxidesTest")]
    [TestCase("смешанный текст сло-", "word", false, TestName = "CyrillicThenLatinStaysTest")]
    [TestCase("mixed text wo-", "рд", false, TestName = "LatinThenCyrillicStaysTest")]
    [TestCase("русский и греческий κοι-", "лада", false, TestName = "GreekThenCyrillicStaysTest")]
    [TestCase("Москва и Санкт-", "Петербург", false, TestName = "UpperCaseStartStaysTest")]
    [TestCase("номер 19-", "й раз", false, TestName = "DigitBeforeTheHyphenStaysTest")]
    [TestCase("тире —", "и дальше", false, TestName = "NoHyphenStaysTest")]
    [TestCase("-", "во", false, TestName = "ALoneHyphenStaysTest")]
    [TestCase("сло-", "", false, TestName = "AnEmptyNextLineStaysTest")]
    [TestCase("СЛО-", "во", false, TestName = "UpperCaseBeforeTheHyphenStaysTest")]
    public void TheRuleTest(string previous, string next, bool joins)
    {
        Assert.That(PdfTextHyphens.Joins(previous, next), Is.EqualTo(joins));
    }

    [Test]
    public void AJoinedWordLosesTheHyphenAndTheBreakTest()
    {
        var lines = PdfTextHyphens.Join(["В сегодняшней Молдове сто-", "ронники присоединения к Румы-", "нии и молдав-", "ского языка."], out var joined);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Is.EqualTo(new[] { "В сегодняшней Молдове сторонники присоединения к Румынии и молдавского языка." }));
            Assert.That(joined, Is.EqualTo(3));
        });
    }

    [Test]
    public void LinesThatDoNotJoinAreLeftAsTheyAreTest()
    {
        string[] lines = ["A Latin line ends in a hyphen-", "ated word.", "Москва и Санкт-", "Петербург."];

        var result = PdfTextHyphens.Join(lines, out var joined);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(lines), "nothing to join, nothing copied");
            Assert.That(joined, Is.Zero);
        });
    }

    [Test]
    public void TheScriptsAreTheUnicodeBlocksOfCyrillicAndGreekTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PdfTextHyphens.Script(new Rune('ж')), Is.EqualTo("Cyrillic"));
            Assert.That(PdfTextHyphens.Script(new Rune('ԁ')), Is.EqualTo("Cyrillic"));
            Assert.That(PdfTextHyphens.Script(new Rune('λ')), Is.EqualTo("Greek"));
            Assert.That(PdfTextHyphens.Script(new Rune('ἀ')), Is.EqualTo("Greek"));
            Assert.That(PdfTextHyphens.Script(new Rune('a')), Is.Null, "Latin is pdf_oxide's");
            Assert.That(PdfTextHyphens.Script(new Rune('é')), Is.Null);
            Assert.That(PdfTextHyphens.Script(new Rune('ա')), Is.Null, "Armenian is not covered");
        });
    }

    #endregion

    #region Shaper Tests

    [Test]
    public void TheShaperJoinsWithinAPageNotAcrossPagesTest()
    {
        var shaped = PdfTextShaper.Shape("первая стра-\nница текста\fвто-\nрая стра-\fница");

        Assert.Multiple(() =>
        {
            Assert.That(shaped.Pages[0], Is.EqualTo(new[] { "первая страница текста" }));
            Assert.That(shaped.Pages[1], Is.EqualTo(new[] { "вторая стра-" }), "a page's last line keeps its hyphen");
            Assert.That(shaped.Pages[2], Is.EqualTo(new[] { "ница" }));
            Assert.That(shaped.HyphensJoined, Is.EqualTo(2));
        });
    }

    [Test]
    public void TheShaperJoinsAfterTheRunningLinesAreGoneTest()
    {
        // A running line between the two halves of a word does not block the join: it is removed
        // first, and the halves meet.
        string[] names = ["один", "два", "три", "четыре", "пять", "шесть"];
        var pages = names.Select(name => $"страница {name} и сло-\nЛетопись долины\nво продолжается {name}").ToList();

        var shaped = PdfTextShaper.Shape(string.Join('\f', pages));

        Assert.Multiple(() =>
        {
            Assert.That(shaped.RunningLinesRemoved, Is.EqualTo(6));
            Assert.That(shaped.Pages.Select(page => page.Single()), Is.EqualTo(names.Select(name => $"страница {name} и слово продолжается {name}")));
            Assert.That(shaped.HyphensJoined, Is.EqualTo(6));
        });
    }

    #endregion
}
