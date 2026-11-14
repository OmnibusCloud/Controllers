using System.Text;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Language;

/// <summary>
/// The identifier counts and ranks n-grams packed into 64-bit keys; the trainer, the model
/// format and the reference semantics use strings. On every document of the golden shard the
/// two forms give the same counts, the same ranking and the same guess.
/// </summary>
[TestFixture]
public sealed class LanguagePackedParityTests
{
    [Test]
    public void PackedCountsAndRankingEqualTheStringFormTest()
    {
        foreach (var text in Documents())
        {
            var strings = new Dictionary<string, long>(StringComparer.Ordinal);
            var packed = new Dictionary<ulong, long>();
            LanguageNgrams.Count(text, strings, LanguageIdentifier.MAX_CHARS);
            LanguageNgrams.CountPacked(text, packed, LanguageIdentifier.MAX_CHARS);

            Assert.That(packed, Has.Count.EqualTo(strings.Count));
            foreach (var (gram, count) in strings)
                Assert.That(packed[LanguageNgrams.Pack(gram)], Is.EqualTo(count), gram);

            var rankedStrings = LanguageNgrams.Rank(strings, LanguageIdentifier.TEXT_PROFILE_SIZE).Select(gram => LanguageNgrams.Pack(gram)).ToList();
            Assert.That(LanguageNgrams.RankPacked(packed, LanguageIdentifier.TEXT_PROFILE_SIZE), Is.EqualTo(rankedStrings));
        }
    }

    [Test]
    public void PackedGuessEqualsTheStringFormGuessTest()
    {
        var model = LanguageModel.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "language-pack-cc-2026-43-16.bin"));
        var identifier = new LanguageIdentifier(model);
        var maxRank = model.Profiles.Max(profile => profile.Grams.Count);

        var compared = 0;
        foreach (var text in Documents())
        {
            Assert.That(identifier.Identify(text), Is.EqualTo(StringFormGuess(model, maxRank, text)));
            compared++;
        }

        Assert.That(compared, Is.GreaterThan(400));
    }

    [TestCase("a", "ab")]
    [TestCase("ab", "abc")]
    [TestCase("abc", "abd")]
    [TestCase("_a", "a_")]
    [TestCase("z", "\u00e9")]
    [TestCase("\u044f", "\u4e2d")]
    public void PackedOrderIsOrdinalOrderTest(string first, string second)
    {
        first = System.Text.RegularExpressions.Regex.Unescape(first);
        second = System.Text.RegularExpressions.Regex.Unescape(second);

        Assert.That(Math.Sign(LanguageNgrams.Pack(first).CompareTo(LanguageNgrams.Pack(second))), Is.EqualTo(Math.Sign(string.CompareOrdinal(first, second))));
    }

    /// <summary>The identification as it was before the packed path: strings throughout.</summary>
    private static LanguageGuess StringFormGuess(LanguageModel model, int maxRank, string text)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        LanguageNgrams.Count(text, counts, LanguageIdentifier.MAX_CHARS);
        var letters = counts.Where(pair => pair.Key.Length == 1).Sum(pair => pair.Value);
        if (letters < LanguageIdentifier.MIN_LETTERS || model.Profiles.Count == 0)
            return LanguageGuess.Unknown;

        var textProfile = LanguageNgrams.Rank(counts, LanguageIdentifier.TEXT_PROFILE_SIZE);
        string? best = null, second = null;
        long bestDistance = long.MaxValue, secondDistance = long.MaxValue;
        foreach (var profile in model.Profiles)
        {
            long distance = 0;
            for (var i = 0; i < textProfile.Count; i++)
            {
                var rank = profile.RankOf(textProfile[i]);
                distance += rank < 0 ? maxRank : Math.Abs(rank - i);
            }

            if (distance < bestDistance || (distance == bestDistance && string.CompareOrdinal(profile.Code, best) < 0))
            {
                second = best;
                secondDistance = bestDistance;
                best = profile.Code;
                bestDistance = distance;
            }
            else if (distance < secondDistance)
            {
                second = profile.Code;
                secondDistance = distance;
            }
        }

        var worst = (double)maxRank * textProfile.Count;
        var margin = second == null ? 1.0 : (secondDistance - bestDistance) / worst;
        var distancePpm = LanguageIdentifier.Ppm(bestDistance, (long)maxRank * textProfile.Count);
        if (model.Acceptance != null && !model.Acceptance.Accepts(best!, distancePpm))
            return new LanguageGuess("", second, margin) { Nearest = best!, DistancePpm = distancePpm, IsUnsupported = true, PastLimit = model.Acceptance.Supports(best!) };

        return new LanguageGuess(best!, second, margin) { Nearest = best!, DistancePpm = distancePpm };
    }

    private static IEnumerable<string> Documents()
    {
        using var reader = WarcReader.Open(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "cc-main-2026-43-00000-head.warc.wet.gz"));
        foreach (var record in reader.ReadRecords().Where(r => r.Type == "conversion"))
            yield return HtmlTextExtractor.CollapseLines(Encoding.UTF8.GetString(record.Payload.Span));
    }
}
