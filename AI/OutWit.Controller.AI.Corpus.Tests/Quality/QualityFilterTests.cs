using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Quality;

/// <summary>
/// The quality filter: a plain article passes, each rule fails on the shape it names, an
/// unspaced script is judged by its letters, the verdict is the same every time, and on the
/// golden shard a plausible share of raw pages survives with the drops accounted for by rule.
/// </summary>
[TestFixture]
public sealed class QualityFilterTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";

    private const string ARTICLE =
        "The committee reviewed the annual report in detail and approved the budget for the coming year.\n" +
        "Members raised questions about the maintenance schedule, the staffing plan and the reserve fund.\n" +
        "The chair thanked the volunteers for their work on the summer programme and the autumn fair.\n" +
        "A vote on the revised bylaws was postponed until every member had received the final draft.\n" +
        "The next meeting will take place in the community hall on the first Tuesday of the month.\n" +
        "Minutes of the previous meeting were approved without amendment after a short discussion.";

    #endregion

    #region Rule Tests

    [Test]
    public void APlainArticlePassesTest()
    {
        var verdict = QualityFilter.Judge(ARTICLE, QualityRules.Spaced);

        Assert.That(verdict.Passed, Is.True, string.Join(", ", verdict.FailedRules));
        Assert.That(verdict.Measures["words"], Is.GreaterThan(50));
    }

    [Test]
    public void TooShortTooLongAndOddWordShapesFailTheirRulesTest()
    {
        var shortText = "Only a handful of words here.";
        var longText = string.Join(' ', Enumerable.Repeat("word", 100_001));
        var longWords = string.Join(' ', Enumerable.Repeat("supercalifragilisticexpialidocious", 60));
        var numbers = string.Join(' ', Enumerable.Range(0, 80).Select(i => i.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(QualityFilter.Judge(shortText, QualityRules.Spaced).FailedRules, Does.Contain("min_words"));
            Assert.That(QualityFilter.Judge(longText, QualityRules.Spaced).FailedRules, Does.Contain("max_words"));
            Assert.That(QualityFilter.Judge(longWords, QualityRules.Spaced).FailedRules, Does.Contain("mean_word_length"));
            Assert.That(QualityFilter.Judge(numbers, QualityRules.Spaced).FailedRules, Does.Contain("alphabetic_words").And.Contain("mean_word_length"));
        });
    }

    [Test]
    public void SymbolsBulletsAndEllipsesFailTheirRulesTest()
    {
        var hashes = string.Join(' ', Enumerable.Repeat("#tag word", 40));
        var bullets = string.Join('\n', Enumerable.Repeat("• a list item with several words in it", 30));
        var ellipses = string.Join('\n', Enumerable.Repeat("a teaser line that trails off into nothing much at all...", 30));

        Assert.Multiple(() =>
        {
            Assert.That(QualityFilter.Judge(hashes, QualityRules.Spaced).FailedRules, Does.Contain("symbol_to_word_ratio"));
            Assert.That(QualityFilter.Judge(bullets, QualityRules.Spaced).FailedRules, Does.Contain("bullet_lines"));
            Assert.That(QualityFilter.Judge(ellipses, QualityRules.Spaced).FailedRules, Does.Contain("ellipsis_lines"));
        });
    }

    [Test]
    public void RepetitionFailsTheDuplicateAndNgramRulesTest()
    {
        var repeatedLines = string.Join('\n', Enumerable.Repeat("the same sentence repeated on every single line of this page", 20));
        var dominantPhrase = string.Join(' ', Enumerable.Repeat("buy now buy now", 40)) + " and some other words to fill the document out a little";

        var lines = QualityFilter.Judge(repeatedLines, QualityRules.Spaced);
        var phrase = QualityFilter.Judge(dominantPhrase, QualityRules.Spaced);

        Assert.Multiple(() =>
        {
            Assert.That(lines.FailedRules, Does.Contain("duplicate_lines").And.Contain("duplicate_line_chars"));
            Assert.That(phrase.FailedRules, Does.Contain("top_ngram_chars_2"));
            Assert.That(phrase.FailedRules, Does.Contain("duplicate_ngram_chars_5"));
        });
    }

    [Test]
    public void UnspacedScriptsAreJudgedByTheirLettersTest()
    {
        const string chinese =
            "委员会详细审查了年度报告并批准了明年的预算。成员们就维护计划、人员配置和储备基金提出了问题。" +
            "主席感谢志愿者在夏季活动和秋季集市中的工作。关于修订章程的表决推迟到每位成员收到最终草案之后。" +
            "下次会议将于下月第一个星期二在社区礼堂举行。上次会议纪要经简短讨论后获得通过，没有修改。";

        var asSpaced = QualityFilter.Judge(chinese, QualityRules.Spaced);
        var asUnspaced = QualityFilter.Judge(chinese, QualityRules.ForLanguage("zho"));

        Assert.Multiple(() =>
        {
            Assert.That(asSpaced.Passed, Is.False, "a space-split sees three giant words");
            Assert.That(asUnspaced.Passed, Is.True, string.Join(", ", asUnspaced.FailedRules));
            Assert.That(asUnspaced.Measures["words"], Is.GreaterThan(100));
            Assert.That(QualityRules.ForLanguage("eng"), Is.SameAs(QualityRules.Spaced));
            Assert.That(QualityRules.ForLanguage("jpn"), Is.SameAs(QualityRules.Unspaced));
        });
    }

    [Test]
    public void VerdictIsDeterministicTest()
    {
        var first = QualityFilter.Judge(ARTICLE, QualityRules.Spaced);
        var second = QualityFilter.Judge(ARTICLE, QualityRules.Spaced);

        Assert.That(second.Measures, Is.EqualTo(first.Measures));
        Assert.That(second.FailedRules, Is.EqualTo(first.FailedRules));
    }

    #endregion

    #region Golden Shard Tests

    [Test]
    public void GoldenShardPagesSurviveAtAPlausibleRateWithDropsByRuleTest()
    {
        var identifier = new LanguageIdentifier(LanguageModel.Load(Fixture(MODEL_FIXTURE)));
        var total = 0;
        var passed = 0;
        var drops = new Dictionary<string, int>(StringComparer.Ordinal);

        using var reader = WarcReader.Open(Fixture(WET_FIXTURE));
        foreach (var record in reader.ReadRecords().Where(r => r.Type == "conversion"))
        {
            var text = Encoding.UTF8.GetString(record.Payload.Span);
            var language = identifier.Identify(text);
            var verdict = QualityFilter.Judge(text, QualityRules.ForLanguage(language.Code));

            total++;
            if (verdict.Passed)
                passed++;
            foreach (var rule in verdict.FailedRules)
                drops[rule] = drops.GetValueOrDefault(rule) + 1;
        }

        TestContext.Out.WriteLine($"{total} WET documents: {passed} pass ({100.0 * passed / total:F1} %); drops by rule: {string.Join(", ", drops.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}={pair.Value}"))}");

        Assert.That((double)passed / total, Is.InRange(0.1, 0.95), "raw crawl pages: some survive, many do not");
    }

    #endregion

    #region Helpers

    private static string Fixture(string name)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    }

    #endregion
}
