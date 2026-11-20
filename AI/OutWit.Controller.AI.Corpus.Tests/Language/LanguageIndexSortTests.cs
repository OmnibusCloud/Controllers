using System.Diagnostics;
using OutWit.Controller.AI.Corpus.Language;

namespace OutWit.Controller.AI.Corpus.Tests.Language;

/// <summary>
/// A language pack's n-grams were the keys of two tables a node builds when the pack loads: each
/// profile's ranks by packed n-gram, and the identifier's index of every profile. A packed
/// n-gram's hash code is its two halves combined, and the pack is the pack author's: a pack whose
/// n-grams all share one hash code put them in one chain, a node paid that chain at every load
/// and every lookup. Both are sorted arrays now, looked up by a binary search.
/// </summary>
[TestFixture]
public sealed class LanguageIndexSortTests
{
    #region Tests

    [Test]
    public void APackWhoseNgramsShareOneHashCodeLoadsAsFastAsAnyOtherTest()
    {
        // 131,072 four-character n-grams whose packed keys all have the same hash code.
        var grams = SameHashCode(512, 256);
        var clock = Stopwatch.StartNew();
        var profile = new LanguageProfile("aaa", grams);
        var identifier = new LanguageIdentifier(new LanguageModel([profile, new LanguageProfile("bbb", grams.Take(1000).ToList())]));
        var guess = identifier.Identify(string.Concat(grams.Take(200)));
        var elapsed = clock.Elapsed;
        TestContext.Out.WriteLine($"{grams.Count} n-grams loaded and looked up in {elapsed.TotalSeconds:F2} s; guess {guess.Code}");

        Assert.Multiple(() =>
        {
            Assert.That(grams.Select(gram => LanguageNgrams.Pack(gram).GetHashCode()).Distinct().Count(), Is.EqualTo(1), "the keys are what they are meant to be");
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "a sort and binary searches, not a chain (it was minutes)");
            Assert.That(profile.RankOf(LanguageNgrams.Pack(grams[12_345])), Is.EqualTo(12_345));
        });
    }

    [Test]
    public void ARankIsTheFirstAProfileListsAnNgramAtTest()
    {
        var profile = new LanguageProfile("aaa", ["th", "he", "th", "e", "the"]);

        Assert.Multiple(() =>
        {
            Assert.That(profile.RankOf(LanguageNgrams.Pack("th")), Is.EqualTo(0));
            Assert.That(profile.RankOf(LanguageNgrams.Pack("he")), Is.EqualTo(1));
            Assert.That(profile.RankOf(LanguageNgrams.Pack("e")), Is.EqualTo(3));
            Assert.That(profile.RankOf(LanguageNgrams.Pack("the")), Is.EqualTo(4));
            Assert.That(profile.RankOf(LanguageNgrams.Pack("x")), Is.EqualTo(-1));
            Assert.That(profile.RankOf("th"), Is.EqualTo(0));
        });
    }

    #endregion

    #region Tools

    /// <summary>Four-character n-grams whose packed keys share one hash code: the third and fourth characters are the first and second with one bit turned.</summary>
    private static List<string> SameHashCode(int firsts, int seconds)
    {
        var grams = new List<string>(firsts * seconds);
        for (var first = 0; first < firsts; first++)
        {
            for (var second = 0; second < seconds; second++)
            {
                var c0 = (char)(0x4E00 + first);
                var c1 = (char)(0x4E00 + 0x1000 + second);
                grams.Add(new string([c0, c1, (char)(c0 ^ 0x0100), (char)(c1 ^ 0x0100)]));
            }
        }

        return grams;
    }

    #endregion
}
