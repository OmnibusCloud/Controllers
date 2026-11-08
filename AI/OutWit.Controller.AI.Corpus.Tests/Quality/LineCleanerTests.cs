using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Quality;

/// <summary>
/// The boilerplate-line stage on the shapes a WET page has: menus, prices and footers go,
/// sentences and long lines stay, for spaced and unspaced scripts; and a WET document keeps
/// its lines on the way in.
/// </summary>
[TestFixture]
public sealed class LineCleanerTests
{
    [TestCase("Оплата и доставка", false)]
    [TestCase("Контакты", false)]
    [TestCase("4490.00 руб.", false)]
    [TestCase("Read more...", false)]
    [TestCase("Read the full story…", false)]
    [TestCase("The water was cold that morning.", true)]
    [TestCase("\"Is this the right way?\" she asked.", true)]
    [TestCase("He said: «this is it.»", true)]
    [TestCase("Three words here.", true)]
    [TestCase("Two words.", false)]
    [TestCase("A long heading without a final mark that still runs past a dozen words in all", true)]
    [TestCase("Home About Products Contact Blog", false)]
    public void SpacedLinesTest(string line, bool kept)
    {
        Assert.That(LineCleaner.Keep(line, QualityRules.Spaced), Is.EqualTo(kept));
    }

    [TestCase("コンテンツへスキップ", false)]
    [TestCase("インスペクション・住宅診断", false)]
    [TestCase("マンションの１１階以上は買わない方がいいって本当？", true)]
    [TestCase("马上来看看吧。", true)]
    [TestCase("设为首页", false)]
    [TestCase("这是一段没有句末标点但是非常非常长的文字它应该被保留下来因为它显然是正文的一部分而不是导航菜单的一个条目", true)]
    public void UnspacedLinesTest(string line, bool kept)
    {
        Assert.That(LineCleaner.Keep(line, QualityRules.Unspaced), Is.EqualTo(kept));
    }

    [Test]
    public void MenuPageKeepsItsArticleTest()
    {
        var page = string.Join('\n',
            "Home", "About us", "Shop", "Cart (0)", "Sign in",
            "How to choose a swimming cap for a child",
            "A swimming cap protects the hair from chlorinated water and keeps it out of the eyes.",
            "Silicone caps last longer than fabric ones, but they are harder to put on for small children.",
            "Price: 12.99 EUR", "Add to cart", "© 2026 Example Shop. All rights reserved.");

        var cleaned = LineCleaner.Clean(page, QualityRules.Spaced, out var removed);

        Assert.Multiple(() =>
        {
            Assert.That(cleaned.Split('\n'), Is.EqualTo(new[]
            {
                "A swimming cap protects the hair from chlorinated water and keeps it out of the eyes.",
                "Silicone caps last longer than fabric ones, but they are harder to put on for small children.",
                "© 2026 Example Shop. All rights reserved."
            }));
            Assert.That(removed, Is.EqualTo(8));
        });
    }

    [Test]
    public void NothingProseLeavesNothingTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LineCleaner.Clean("Home\nAbout\nContact", QualityRules.Spaced, out var removed), Is.Empty);
            Assert.That(removed, Is.EqualTo(3));
            Assert.That(LineCleaner.Clean("", QualityRules.Spaced, out _), Is.Empty);
        });
    }

    [Test]
    public void WetLinesSurviveTheCollapseTest()
    {
        var wet = "  First   line \r\n\r\n\tSecond line\n\n   \nThird line  ";

        Assert.Multiple(() =>
        {
            Assert.That(HtmlTextExtractor.CollapseLines(wet), Is.EqualTo("First line\nSecond line\nThird line"));
            Assert.That(HtmlTextExtractor.Collapse(wet), Is.EqualTo("First line Second line Third line"), "Collapse itself still flattens; the WET path uses CollapseLines");
        });
    }
}
