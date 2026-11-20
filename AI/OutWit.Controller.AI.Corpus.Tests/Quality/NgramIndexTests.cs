using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Quality;

/// <summary>
/// The quality filter's n-gram index counted a page's n-grams in tables keyed by a polynomial
/// hash of the words' ids, which the page could choose its words against. It numbers the
/// n-grams by sorting now. These hold it to the earlier table's answers, n-gram length by
/// n-gram length, on pages made to repeat themselves in every way the filter measures.
/// </summary>
[TestFixture]
public sealed class NgramIndexTests
{
    #region Tests

    [TestCase(1, 0, 3)]
    [TestCase(2, 1, 12)]
    [TestCase(3, 10, 60)]
    [TestCase(4, 300, 40)]
    [TestCase(5, 2000, 400)]
    [TestCase(6, 5000, 5000)]
    public void TheIndexAnswersAsTheEarlierTableDidTest(int seed, int words, int vocabulary)
    {
        var page = Page(new Random(seed), words, vocabulary);
        var index = new NgramIndex(page);
        var table = new CorpusTestNgramTable(page);

        Assert.That(index.Count, Is.EqualTo(table.Count));
        for (var n = 1; n <= 11; n++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(index.MostFrequent(n), Is.EqualTo(table.MostFrequent(n)), $"the most frequent {n}-gram");
                Assert.That(index.RepeatedChars(n), Is.EqualTo(table.RepeatedChars(n)), $"the repeats of {n}-grams");
            });
        }
    }

    [Test]
    public void TheIndexAnswersTheSameInAnyOrderOfQuestionsTest()
    {
        // The filter asks 2 to 4, then 5 to 10; an index asked out of order answers as it.
        var page = Page(new Random(9), 3000, 50);
        var forward = new NgramIndex(page);
        var backward = new NgramIndex(page);

        var asked = Enumerable.Range(1, 10).Select(n => (forward.MostFrequent(n), forward.RepeatedChars(n))).ToList();
        var askedBack = Enumerable.Range(1, 10).Reverse().Select(n => (backward.MostFrequent(n), backward.RepeatedChars(n))).Reverse().ToList();

        Assert.That(askedBack, Is.EqualTo(asked));
    }

    #endregion

    #region Tools

    /// <summary>A page of words from a small vocabulary, with runs of words and of phrases repeated, as boilerplate repeats.</summary>
    private static List<string> Page(Random random, int words, int vocabulary)
    {
        var page = new List<string>(words);
        while (page.Count < words)
        {
            switch (random.Next(4))
            {
                case 0 when page.Count > 12:
                    // A phrase said again.
                    var length = random.Next(2, 12);
                    var from = random.Next(0, page.Count - length);
                    page.AddRange(page.GetRange(from, length));
                    break;
                case 1:
                    // A word said several times.
                    var word = $"w{random.Next(vocabulary)}";
                    page.AddRange(Enumerable.Repeat(word, random.Next(1, 6)));
                    break;
                default:
                    page.Add($"w{random.Next(vocabulary)}");
                    break;
            }
        }

        return page.Take(words).ToList();
    }

    #endregion
}
