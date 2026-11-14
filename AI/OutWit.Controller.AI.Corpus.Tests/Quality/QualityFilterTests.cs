using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Quality;

/// <summary>
/// The quality filter: a plain article passes, each rule fails on the shape it names, every
/// measure is the number DataTrove's code gives on the same text, the verdict is the same every
/// time, and on the golden shard a plausible share of raw pages survives with the drops
/// accounted for by rule. The rules of each language: <see cref="QualityRulesTests"/>.
/// </summary>
[TestFixture]
public sealed class QualityFilterTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string MODEL_FIXTURE = "language-pack-cc-2026-43-16.bin";

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
    public void LoremIpsumDropsThePageTest()
    {
        var page = ARTICLE + "\nLorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor.";

        Assert.That(QualityFilter.Judge(page, QualityRules.Spaced).FailedRules, Is.EqualTo(new[] { "lorem_ipsum" }));
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

    #region Reference Definition Tests

    // The expected values below are what DataTrove's own functions (find_all_duplicate,
    // find_top_duplicate over get_n_grams, find_duplicates; gopher_repetition_filter.py) return
    // on the same texts split into whitespace words, over len(text).

    [Test]
    public void APhraseSaidTwiceCountsOnlyItsRepeatTest()
    {
        // Twenty words, then the first ten again: the repeat is "alpha ... kappa" (47 letters) of
        // a 162-character text. Counting both occurrences would make it 94 of 133 word letters.
        const string text =
            "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon\n" +
            "alpha beta gamma delta epsilon zeta eta theta iota kappa";

        var measures = QualityFilter.Judge(text, QualityRules.Spaced).Measures;

        Assert.Multiple(() =>
        {
            Assert.That(text, Has.Length.EqualTo(162));
            Assert.That(measures["duplicate_ngram_chars_5"], Is.EqualTo(47.0 / 162).Within(1e-12));
            Assert.That(measures["duplicate_ngram_chars_10"], Is.EqualTo(47.0 / 162).Within(1e-12));
            Assert.That(measures["duplicate_ngram_chars_6"], Is.EqualTo(30.0 / 162).Within(1e-12), "one 6-word repeat, then a 4-word tail too short for another");
        });
    }

    [Test]
    public void RepeatsAreCountedOnceAndSkippedPastTest()
    {
        // Positions 0..4 are new; at 5 the first 5-gram repeats (5 letters, skip to 10), at 10 again.
        const string text = "a b c d e a b c d e a b c d e";

        var measures = QualityFilter.Judge(text, QualityRules.Spaced).Measures;

        Assert.That(measures["duplicate_ngram_chars_5"], Is.EqualTo(10.0 / 29).Within(1e-12));
    }

    [Test]
    public void TheMostFrequentNgramIsChosenByCountTest()
    {
        // "a b" occurs three times (3 characters each, the space included); the long pair occurs
        // twice and covers more characters, but it is not the most frequent.
        const string text = "a b a b a b xxxxxxxxxx yyyyyyyyyy xxxxxxxxxx yyyyyyyyyy";

        var measures = QualityFilter.Judge(text, QualityRules.Spaced).Measures;

        Assert.That(measures["top_ngram_chars_2"], Is.EqualTo(9.0 / 55).Within(1e-12));
    }

    [Test]
    public void TopNgramTiesGoToTheFirstTest()
    {
        // "aa bb" and "cc dd" both occur twice; "aa bb" comes first. Every trigram occurs once.
        const string text = "aa bb cc dd aa bb ee ff cc dd gg hh";

        var measures = QualityFilter.Judge(text, QualityRules.Spaced).Measures;

        Assert.Multiple(() =>
        {
            Assert.That(measures["top_ngram_chars_2"], Is.EqualTo(10.0 / 35).Within(1e-12));
            Assert.That(measures["top_ngram_chars_3"], Is.EqualTo(8.0 / 35).Within(1e-12));
        });
    }

    [Test]
    public void DuplicateLineCharactersAreCountedAgainstTheWholeTextTest()
    {
        const string text = "first line here\nsecond line here\nfirst line here";

        var measures = QualityFilter.Judge(text, QualityRules.Spaced).Measures;

        Assert.Multiple(() =>
        {
            Assert.That(measures["duplicate_lines"], Is.EqualTo(1.0 / 3).Within(1e-12));
            Assert.That(measures["duplicate_line_chars"], Is.EqualTo(15.0 / 48).Within(1e-12));
        });
    }

    [Test]
    public void HashesAndEllipsesAreJudgedSeparatelyTest()
    {
        // 100 words with 6 hash marks and 6 ellipses: each ratio is 0.06, under Gopher's 0.1 for
        // "either the hash symbol or the ellipsis" (summed they would be 0.12); 12 hash marks fail.
        var mixed = string.Join(' ', Enumerable.Range(0, 100).Select(i => i < 6 ? "#tag" : i < 12 ? "wait..." : "word"));
        var hashes = string.Join(' ', Enumerable.Range(0, 100).Select(i => i < 12 ? "#tag" : "word"));

        var mixedVerdict = QualityFilter.Judge(mixed, QualityRules.Spaced);
        var hashesVerdict = QualityFilter.Judge(hashes, QualityRules.Spaced);

        Assert.Multiple(() =>
        {
            Assert.That(mixedVerdict.Measures["symbol_to_word_ratio"], Is.EqualTo(0.06).Within(1e-12));
            Assert.That(mixedVerdict.FailedRules, Does.Not.Contain("symbol_to_word_ratio"));
            Assert.That(hashesVerdict.Measures["symbol_to_word_ratio"], Is.EqualTo(0.12).Within(1e-12));
            Assert.That(hashesVerdict.FailedRules, Does.Contain("symbol_to_word_ratio"));
        });
    }

    #endregion

    #region Golden Shard Tests

    [Test]
    public void GoldenShardPagesSurviveAtAPlausibleRateWithDropsByRuleTest()
    {
        var model = LanguageModel.Load(Fixture(MODEL_FIXTURE));
        var identifier = new LanguageIdentifier(model);
        var total = 0;
        var passed = 0;
        var drops = new Dictionary<string, int>(StringComparer.Ordinal);

        using var reader = WarcReader.Open(Fixture(WET_FIXTURE));
        foreach (var record in reader.ReadRecords().Where(r => r.Type == "conversion"))
        {
            var text = Encoding.UTF8.GetString(record.Payload.Span);
            var language = identifier.Identify(text);
            var verdict = QualityFilter.Judge(text, model.Rules.For(language.Code));

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
