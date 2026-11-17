using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Text.Casing;

namespace OutWit.Controller.AI.Corpus.Tests.Text;

/// <summary>
/// Audit finding M-2: the language n-grams and the MinHash tokens were lower-cased by
/// <c>Rune.ToLowerInvariant</c>, which .NET answers from ICU, from Windows NLS or from its own
/// table depending on the machine; the three differ for hundreds of code points, so two honest
/// nodes could sign one document differently. The controller now lower-cases by a table of its
/// own, made from one named Unicode version and pinned here, and compares ASCII names as ASCII.
/// The pins below are the same numbers on Windows, Linux and macOS: that is the proof.
/// </summary>
[TestFixture]
public sealed class CorpusCasingM2Tests
{
    #region Constants

    /// <summary>The lower-case table: Unicode 16.0.0, Simple_Lowercase_Mapping, U+0130 kept.</summary>
    private const string TABLE_SHA256 = "829c36f2e4051a0b3da59bc1861932cd64d035e7147c29c961cf3d8f847fc5e8";

    /// <summary>Capitals whose lower case the libraries disagree on: Unicode 16 additions (an ICU before it keeps them) and letters Windows NLS keeps.</summary>
    private static readonly (int Upper, int Lower)[] DISPUTED =
    [
        (0x1C89, 0x1C8A), (0xA7CB, 0x0264), (0xA7CC, 0xA7CD), (0xA7DA, 0xA7DB), (0xA7DC, 0x019B), (0x10D50, 0x10D70), (0x10D65, 0x10D85),
        (0x0524, 0x0525), (0x01C5, 0x01C6), (0x037F, 0x03F3), (0x03F4, 0x03B8), (0x0526, 0x0527), (0x052E, 0x052F), (0x10C7, 0x2D27), (0x13A0, 0xAB70)
    ];

    /// <summary>
    /// A text of the disputed capitals, in words long enough to shingle: its signature and its
    /// n-grams are the golden values of the edge characters.
    /// </summary>
    private static readonly string EDGE_TEXT = string.Join(' ', Enumerable.Range(0, 40).Select(word =>
        string.Concat(Enumerable.Range(0, 5).Select(letter => char.ConvertFromUtf32(DISPUTED[(word * 3 + letter * 7) % DISPUTED.Length].Upper))) + "x"));

    private const string EDGE_SIGNATURE_SHA256 = "9939fed2735d867a89d77e2f6b72b9bc11917d6a17426da43b2464e34e13b392";
    private const string EDGE_NGRAMS_SHA256 = "403aab111eca520916fd9f99f4784c1475541ce21d70407bc714c4bb7c71d6a6";

    #endregion

    #region Table Tests

    [Test]
    public void M2TheTableIsTheOneGeneratedFromUnicode16Test()
    {
        var pairs = CorpusCasingTable.Pairs;
        var numbers = pairs.Length;
        var text = new StringBuilder();
        for (var index = 0; index < pairs.Length; index += 2)
            text.Append(pairs[index].ToString("X4")).Append(';').Append(pairs[index + 1].ToString("X4")).Append('\n');

        Assert.Multiple(() =>
        {
            Assert.That(CorpusCasing.UNICODE_VERSION, Is.EqualTo("16.0.0"));
            Assert.That(CorpusCasingTable.SOURCE, Is.EqualTo("https://www.unicode.org/Public/16.0.0/ucd/UnicodeData.txt"));
            Assert.That(CorpusCasingTable.SOURCE_SHA256, Is.EqualTo("ff58e5823bd095166564a006e47d111130813dcf8bf234ef79fa51a870edb48f"));
            Assert.That(CorpusCasingTable.MAPPINGS, Is.EqualTo(1459));
            Assert.That(numbers, Is.EqualTo(2 * CorpusCasingTable.MAPPINGS));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(text.ToString()))), Is.EqualTo(TABLE_SHA256), "the table is not the generated one");
            Assert.That(CorpusCasing.TABLE_SHA256, Is.EqualTo(TABLE_SHA256), "the constant the stage set's fingerprint carries");
        });
    }

    [Test]
    public void M2TheMappingsAreSimpleLowerCaseOfThatVersionTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (upper, lower) in DISPUTED)
                Assert.That(CorpusCasing.ToLower(new Rune(upper)).Value, Is.EqualTo(lower), $"U+{upper:X4}");

            Assert.That(CorpusCasing.ToLower(new Rune('A')).Value, Is.EqualTo('a'));
            Assert.That(CorpusCasing.ToLower(new Rune('z')).Value, Is.EqualTo('z'));
            Assert.That(CorpusCasing.ToLower(new Rune('7')).Value, Is.EqualTo('7'));
            Assert.That(CorpusCasing.ToLower(new Rune(0x00C9)).Value, Is.EqualTo(0x00E9), "E acute");
            Assert.That(CorpusCasing.ToLower(new Rune(0x0416)).Value, Is.EqualTo(0x0436), "Cyrillic ZHE");
            Assert.That(CorpusCasing.ToLower(new Rune(0x03A3)).Value, Is.EqualTo(0x03C3), "Greek SIGMA: the simple mapping, no final form");
            Assert.That(CorpusCasing.ToLower(new Rune(0x1E9E)).Value, Is.EqualTo(0x00DF), "capital sharp s");
            Assert.That(CorpusCasing.ToLower(new Rune(0x212A)).Value, Is.EqualTo('k'), "KELVIN SIGN");
            Assert.That(CorpusCasing.ToLower(new Rune(0x10400)).Value, Is.EqualTo(0x10428), "Deseret, above the BMP");
            Assert.That(CorpusCasing.ToLower(new Rune(0x0130)).Value, Is.EqualTo(0x0130), "the one exception: I with dot above is kept, as .NET has always kept it on every platform");
            Assert.That(CorpusCasing.ToLower(new Rune(0x4E2D)).Value, Is.EqualTo(0x4E2D), "no case");
            Assert.That(CorpusCasing.ToLower('Ж'), Is.EqualTo('ж'));
            Assert.That(CorpusCasing.ToLower('\uD801'), Is.EqualTo('\uD801'), "half of a pair is itself");
        });
    }

    [Test]
    public void M2EveryMappingAgreesWithTheRuntimesOwnDataWhereItHasTheLetterTest()
    {
        // The runtime's own Unicode data (not the machine's library) says which code points are
        // upper-case letters; each of them the table maps must land on a lower-case letter, and
        // lower-casing is idempotent. A table from a wrong column or a wrong file fails here.
        var pairs = CorpusCasingTable.Pairs;
        var wrong = new List<string>();
        for (var index = 0; index < pairs.Length; index += 2)
        {
            var from = new Rune(pairs[index]);
            var to = new Rune(pairs[index + 1]);
            if (CorpusCasing.ToLower(to) != to)
                wrong.Add($"U+{from.Value:X4} maps to U+{to.Value:X4}, which maps on");
            if (Rune.GetUnicodeCategory(from) == System.Globalization.UnicodeCategory.LowercaseLetter)
                wrong.Add($"U+{from.Value:X4} is a lower-case letter and is mapped");
        }

        Assert.That(wrong, Is.Empty);
    }

    #endregion

    #region Product Tests

    [Test]
    public void M2ADocumentIsSignedTheSameWhateverLibraryTheNodeHasTest()
    {
        // The capitals and their lower case are one token: on a machine whose library does not
        // know the mapping (an ICU before Unicode 16 for the first seven, Windows NLS for the
        // rest), the capital stayed a capital and the signatures differed.
        var lower = string.Concat(EDGE_TEXT.EnumerateRunes().Select(rune => Lower(rune)));

        var upperCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var lowerCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        LanguageNgrams.Count(EDGE_TEXT, upperCounts);
        LanguageNgrams.Count(lower, lowerCounts);
        var packedUpper = new Dictionary<ulong, long>();
        var packedLower = new Dictionary<ulong, long>();
        LanguageNgrams.CountPacked(EDGE_TEXT, packedUpper);
        LanguageNgrams.CountPacked(lower, packedLower);

        Assert.Multiple(() =>
        {
            Assert.That(lower, Is.Not.EqualTo(EDGE_TEXT));
            Assert.That(MinHashSigner.Sign(EDGE_TEXT), Is.EqualTo(MinHashSigner.Sign(lower)), "MinHash tokens");
            Assert.That(upperCounts, Is.EquivalentTo(lowerCounts), "language n-grams");
            Assert.That(packedUpper, Is.EquivalentTo(packedLower), "language n-grams, packed");
        });
    }

    [Test]
    public void M2TheEdgeCharactersHaveGoldenValuesTest()
    {
        // The same bytes on every machine: these two hashes are computed on Windows, Linux and
        // macOS in CI, and each of the three libraries would give another for some of the text.
        var signature = MinHashSigner.ToBytes(MinHashSigner.Sign(EDGE_TEXT));
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        LanguageNgrams.Count(EDGE_TEXT, counts);
        var ngrams = string.Join('\n', counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}\t{pair.Value}"));

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(signature)), Is.EqualTo(EDGE_SIGNATURE_SHA256), "the MinHash signature of the edge text");
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ngrams))), Is.EqualTo(EDGE_NGRAMS_SHA256), "the n-grams of the edge text");
        });
    }

    #endregion

    #region ASCII Tests

    [Test]
    public void M2AnAsciiPhraseIsMatchedByAsciiLettersOnlyTest()
    {
        // OrdinalIgnoreCase folds by the machine's library: LATIN SMALL LETTER LONG S and DOTLESS I
        // upper-case to S and I there, so "javaſcript" was "javascript" on some machines.
        Assert.Multiple(() =>
        {
            Assert.That(CorpusCasing.ContainsIgnoreAsciiCase("Please enable JavaScript.", "javascript"), Is.True);
            Assert.That(CorpusCasing.ContainsIgnoreAsciiCase("JAVASCRIPT", "javascript"), Is.True);
            Assert.That(CorpusCasing.ContainsIgnoreAsciiCase("javaſcript", "javascript"), Is.False, "long s is not s");
            Assert.That(CorpusCasing.ContainsIgnoreAsciiCase("cookıe", "cookie"), Is.False, "dotless i is not i");
            Assert.That(CorpusCasing.ContainsIgnoreAsciiCase("cooKie", "cookie"), Is.False, "the Kelvin sign is not k");
            Assert.That(CorpusCasing.ContainsIgnoreAsciiCase("no such phrase", "javascript"), Is.False);
            Assert.That(CorpusCasing.ContainsIgnoreAsciiCase("short", "longer than the text"), Is.False);
            Assert.That(CorpusCasing.IndexOfIgnoreAsciiCase("aXbXc", "x", 2), Is.EqualTo(3));
            Assert.That(CorpusCasing.EqualsIgnoreAsciiCase("Content-Length", "content-length"), Is.True);
            Assert.That(CorpusCasing.EqualsIgnoreAsciiCase("WARC-Identıfied-Payload-Type", "WARC-Identified-Payload-Type"), Is.False);
            Assert.That(CorpusCasing.StartsWithIgnoreAsciiCase("</SCRIPT >", "</script"), Is.True);
            Assert.That(CorpusCasing.ToLowerAscii("TEXT/HTML; É"), Is.EqualTo("text/html; É"), "only A to Z");
            Assert.That(CorpusCasing.ToLowerAscii("already lower"), Is.SameAs("already lower"));
        });
    }

    [Test]
    public void M2TheStagesThatCompareAsciiPhrasesUseItTest()
    {
        var rules = QualityRules.Spaced;

        Assert.Multiple(() =>
        {
            Assert.That(LineCleaner.IsBoilerplate("This site uses COOKIES to improve your experience on every page."), Is.True);
            Assert.That(LineCleaner.IsBoilerplate("This site uses cookıes to improve your experience on every page."), Is.False, "another letter, on every machine");
            Assert.That(QualityFilter.Judge(string.Join(' ', Enumerable.Repeat("Lorem Ipsum dolor sit amet and so on.", 30)), rules).FailedRules, Does.Contain("lorem_ipsum"));
            Assert.That(HtmlTextExtractor.Extract("<html><body><p>shown</p><p STYLE=\"DISPLAY:NONE\">hidden</p><SCRIPT>var a;</SCRIPT></body></html>"), Is.EqualTo("shown"));
        });
    }

    #endregion

    #region Tools

    private static string Lower(Rune rune)
    {
        foreach (var (upper, lower) in DISPUTED)
        {
            if (rune.Value == upper)
                return char.ConvertFromUtf32(lower);
        }

        return rune.ToString();
    }

    #endregion
}
