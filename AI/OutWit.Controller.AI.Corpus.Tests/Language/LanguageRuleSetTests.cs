using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Tests.Language;

/// <summary>
/// Language rule data as data: the frozen rule data of WITLID1 models holds exactly Corpus
/// 0.1.14's tables (pinned, so it can never drift); rule data is validated when read — a script
/// that is not an ISO 15924 code, a stop word with whitespace, a threshold the controller does
/// not have, a wrong count; and a source folder of a language pack reads as its rule data.
/// </summary>
[TestFixture]
public sealed class LanguageRuleSetTests
{
    #region Constants

    /// <summary>The digest of the WITLID1 rule data's canonical description (<see cref="Describe"/>).</summary>
    private const string WITLID1_RULES_SHA256 = "13593f1704fddbbc94f44b9e5f1404c46ca9fdfb37dc799620f682c2d07b3fd7";

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), "corpus-language-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_directory))
            Directory.Delete(m_directory, recursive: true);
    }

    #endregion

    #region Witlid1 Tests

    [Test]
    public void TheWitlid1RuleDataIsFrozenTest()
    {
        var set = LanguageRuleSet.Witlid1;
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Describe(set))));

        Assert.Multiple(() =>
        {
            Assert.That(set.Languages.Select(language => language.Code), Is.EqualTo(new[]
            {
                "bod", "ces", "cmn", "deu", "eng", "fas", "fra", "ita", "jpn", "khm", "lao", "mya", "nld", "pol", "por", "rus", "spa", "tha", "tur", "ukr", "vie", "yue", "zho"
            }));
            Assert.That(set.Languages.Count(language => language.StopWords.Count > 0), Is.EqualTo(17));
            Assert.That(set.For("eng").StopWords, Is.EquivalentTo(new[] { "the", "be", "to", "of", "and", "that", "have", "with" }), "Gopher's eight");
            Assert.That(set.For("jpn").MaxDuplicateLineRatio, Is.EqualTo(0.328));
            Assert.That(set.For("jpn").MaxTopNgramCharRatio, Is.EqualTo(new[] { 0.239, 0.196, 0.172 }));
            Assert.That(set.For("cmn").StopWords, Is.EquivalentTo(set.For("zho").StopWords));
            Assert.That(new[] { "bod", "khm", "lao", "mya", "tha", "yue" }.All(code => ReferenceEquals(set.For(code), QualityRules.Unspaced)), Is.True, "no list: the script's defaults");
            Assert.That(digest, Is.EqualTo(WITLID1_RULES_SHA256), $"the WITLID1 rule data moved: {digest}");
        });
    }

    #endregion

    #region Validation Tests

    [Test]
    public void MalformedRuleDataIsRefusedTest()
    {
        IReadOnlyList<double> one = [0.5];

        Assert.Multiple(() =>
        {
            Assert.That(() => new LanguageRules("deu", "latin", [], 0, []), Throws.ArgumentException, "not an ISO 15924 code");
            Assert.That(() => new LanguageRules("deu", "Latn", ["two words"], 1, []), Throws.ArgumentException, "a stop word with a space");
            Assert.That(() => new LanguageRules("deu", "Latn", ["und"], 2, []), Throws.ArgumentException, "more stop words required than listed");
            Assert.That(() => new LanguageRules("deu", "Latn", [], 0, [new("max_wordz", one)]), Throws.ArgumentException, "an unknown threshold");
            Assert.That(() => new LanguageRules("deu", "Latn", [], 0, [new(QualityRuleOverrides.TOP_NGRAM_CHARS, one)]), Throws.ArgumentException, "three values needed");
            Assert.That(() => new LanguageRules("deu", "Latn", [], 0, [new("min_words", [12.5])]), Throws.ArgumentException, "a word count is whole");
            Assert.That(() => new LanguageRules("deu", "Latn", [], 0, [new("max_bullet_line_ratio", [-1.0])]), Throws.ArgumentException, "negative");
            Assert.That(() => new LanguageRules("", "Latn", [], 0, []), Throws.ArgumentException, "no code");
            Assert.That(() => new LanguageRuleSet([new LanguageRules("deu", "Latn", [], 0, []), new LanguageRules("deu", "Latn", [], 0, [])]), Throws.ArgumentException, "a language twice");
        });
    }

    [Test]
    public void TheJsonFormIsStrictTest()
    {
        static LanguageRules Read(string json, IReadOnlyList<string>? stopWords = null)
        {
            using var document = JsonDocument.Parse(json);
            return LanguageRulesJson.Read("xxx", document.RootElement, stopWords);
        }

        var rules = Read("""{ "name": "X", "script": "Thai", "stop_words": ["ก", "ข"], "quality": { "min_words": 80, "duplicate_ngram_chars": [0.2, 0.2, 0.2, 0.2, 0.2, 0.2] }, "notes": ["why"] }""");

        Assert.Multiple(() =>
        {
            Assert.That(rules.Script, Is.EqualTo("Thai"));
            Assert.That(rules.MinStopWords, Is.EqualTo(LanguageRules.DEFAULT_MIN_STOP_WORDS), "two by default with a list");
            Assert.That(rules.Quality.CountsLetters, Is.True);
            Assert.That(rules.Quality.MinWords, Is.EqualTo(80));
            Assert.That(rules.Quality.MaxDuplicateNgramCharRatio, Is.All.EqualTo(0.2));
            Assert.That(rules.Quality.MaxTopNgramCharRatio, Is.EqualTo(QualityRules.Unspaced.MaxTopNgramCharRatio), "what is not set is the script's default");
            Assert.That(Read("""{ "script": "Latn" }""").MinStopWords, Is.Zero, "no list, no rule");
            Assert.That(() => Read("""{ "script": "Latn", "stopwords": [] }"""), Throws.InstanceOf<FormatException>().With.Message.Contains("stopwords"));
            Assert.That(() => Read("""{ "script": "Latn", "quality": { "min_word": 3 } }"""), Throws.InstanceOf<FormatException>().With.Message.Contains("min_word"));
            Assert.That(() => Read("""{ "script": 5 }"""), Throws.InstanceOf<FormatException>());
            Assert.That(() => Read("""{ "script": "Latn", "stop_words": ["a"] }""", ["b"]), Throws.InstanceOf<FormatException>(), "listed twice");
        });
    }

    #endregion

    #region Source Folder Tests

    [Test]
    public void ASourceFolderReadsAsItsRuleDataTest()
    {
        Write("aaa/language.json", """{ "name": "A", "script": "Latn", "notes": ["a test language"] }""");
        Write("aaa/stopwords.txt", "# a comment\n\nthe\r\nand\n  of  \n");
        Write("bbb/language.json", """{ "script": "Hani", "quality": { "max_duplicate_line_ratio": 0.25 } }""");

        var set = LanguagePackSource.Read(m_directory);

        Assert.Multiple(() =>
        {
            Assert.That(set.Languages.Select(language => language.Code), Is.EqualTo(new[] { "aaa", "bbb" }));
            Assert.That(set.For("aaa").StopWords, Is.EquivalentTo(new[] { "the", "and", "of" }), "comments, blank lines, CR and padding are not words");
            Assert.That(set.For("aaa").MinStopWords, Is.EqualTo(2));
            Assert.That(set.For("bbb").CountsLetters, Is.True);
            Assert.That(set.For("bbb").MaxDuplicateLineRatio, Is.EqualTo(0.25));
        });
    }

    [Test]
    public void AMalformedSourceFolderIsRefusedTest()
    {
        Assert.That(() => LanguagePackSource.Read(m_directory), Throws.InstanceOf<FormatException>(), "no language");

        Write("Bad/language.json", """{ "script": "Latn" }""");
        Assert.That(() => LanguagePackSource.Read(m_directory), Throws.InstanceOf<FormatException>(), "a folder name is a code");

        Directory.Delete(Path.Combine(m_directory, "Bad"), recursive: true);
        Directory.CreateDirectory(Path.Combine(m_directory, "ccc"));
        Assert.That(() => LanguagePackSource.Read(m_directory), Throws.InstanceOf<FormatException>().With.Message.Contains("language.json"));
    }

    [Test]
    public void TheCommittedSourceDataIsTheRuleDataOf0114Test()
    {
        // The Assets project's languages/ folder, the source of the reference pack: the stop-word
        // lists and thresholds of the 16 languages, as 0.1.14 had them in code.
        var directory = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "OutWit.Controller.AI.Corpus.Assets", "languages"));
        Assume.That(Directory.Exists(directory), Is.True, directory);

        var set = LanguagePackSource.Read(directory);

        Assert.Multiple(() =>
        {
            Assert.That(set.Languages, Has.Count.EqualTo(16));
            foreach (var language in set.Languages)
            {
                Assert.That(LanguageRuleSet.Witlid1.TryGet(language.Code, out var frozen), Is.True, language.Code);
                Assert.That(language.StopWords, Is.EqualTo(frozen.StopWords), language.Code);
                Assert.That(language.MinStopWords, Is.EqualTo(frozen.MinStopWords), language.Code);
                Assert.That(language.Script, Is.EqualTo(frozen.Script), language.Code);
            }
        });
    }

    #endregion

    #region Tools

    private static string Describe(LanguageRuleSet set)
    {
        var builder = new StringBuilder();
        foreach (var language in set.Languages)
        {
            builder.Append(language.Code).Append('|').Append(language.Script).Append('|').Append(language.MinStopWords).Append('|')
                .AppendJoin(',', language.StopWords).Append('|');
            foreach (var (key, values) in language.Overrides)
                builder.Append(key).Append('=').AppendJoin(',', values.Select(value => BitConverter.DoubleToInt64Bits(value))).Append(';');
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(m_directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    #endregion
}
