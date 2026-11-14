using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Quality;

/// <summary>
/// The boilerplate-line stage on the shapes a WET page has: menus, prices and footers go,
/// sentences and long lines stay, for spaced and unspaced scripts; JavaScript notices, cookie
/// banners and policy lines go whatever their shape; and a WET document keeps its lines on
/// the way in.
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
    [TestCase("He said: «this is the place.»", true)]
    [TestCase("Five words make a sentence.", true)]
    [TestCase("Four words only here.", false)]
    [TestCase("Comments are closed.", false)]
    [TestCase("Two words.", false)]
    [TestCase("A long heading without a final mark that still runs past a dozen words in all", true)]
    [TestCase("Home About Products Contact Blog", false)]
    public void SpacedLinesTest(string line, bool kept)
    {
        Assert.That(LineCleaner.Keep(line, QualityRules.Spaced), Is.EqualTo(kept));
    }

    [TestCase("Please enable JavaScript in your browser to complete this form.")]
    [TestCase("This website uses cookies to enhance your browsing experience and to analyse our traffic.")]
    [TestCase("We are using cookies to give you the best experience on our website.")]
    [TestCase("Diese Website verwendet Cookies, um Ihre Erfahrung zu verbessern, während Sie navigieren.")]
    [TestCase("By submitting this form, I agree to the Terms of Use and the Privacy Policy of this website.")]
    public void JavaScriptCookieAndPolicyLinesGoTest(string line)
    {
        Assert.Multiple(() =>
        {
            Assert.That(LineCleaner.IsProseShaped(line, QualityRules.Spaced), Is.True, "the line has the shape of prose");
            Assert.That(LineCleaner.Keep(line, QualityRules.Spaced), Is.False);
        });
    }

    [Test]
    public void ALineWithAThousandCharacterWordGoesTest()
    {
        var blob = "data:image/png;base64," + new string('A', LineCleaner.MAX_WORD_LENGTH);
        var line = $"The picture below shows the result of the first experiment {blob} in the lab.";

        Assert.Multiple(() =>
        {
            Assert.That(LineCleaner.Keep(line, QualityRules.Spaced), Is.False);
            Assert.That(LineCleaner.Keep(line.Replace(blob, "image", StringComparison.Ordinal), QualityRules.Spaced), Is.True);
        });
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
            "Price: 12.99 EUR", "Add to cart", "Comments are closed.",
            "This shop uses cookies to remember your basket and to measure how the site is used.",
            "© 2026 Example Shop. All rights reserved.");

        var cleaned = LineCleaner.Clean(page, QualityRules.Spaced, out var removed);

        Assert.Multiple(() =>
        {
            Assert.That(cleaned.Split('\n'), Is.EqualTo(new[]
            {
                "A swimming cap protects the hair from chlorinated water and keeps it out of the eyes.",
                "Silicone caps last longer than fabric ones, but they are harder to put on for small children.",
                "© 2026 Example Shop. All rights reserved."
            }));
            Assert.That(removed, Is.EqualTo(10));
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
