using System.Security.Cryptography;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Tests.Signatures;

/// <summary>
/// MinHash signatures: the same text gives the same signature on every run and platform (the
/// committed digest is the CI legs' common pin), near-duplicates score high, unrelated texts
/// low, band keys collide for near-duplicates, and shingling is case- and punctuation-blind.
/// </summary>
[TestFixture]
public sealed class MinHashSignerTests
{
    #region Constants

    private const string ARTICLE =
        "The committee reviewed the annual report in detail and approved the budget for the coming year. " +
        "Members raised questions about the maintenance schedule, the staffing plan and the reserve fund. " +
        "The chair thanked the volunteers for their work on the summer programme and the autumn fair. " +
        "A vote on the revised bylaws was postponed until every member had received the final draft. " +
        "The next meeting will take place in the community hall on the first Tuesday of the month.";

    private const string NEAR_DUPLICATE =
        "The committee reviewed the annual report in detail and approved the budget for the coming year. " +
        "Members raised questions about the maintenance schedule, the staffing plan and the reserve fund. " +
        "The chair thanked the volunteers for their work on the summer programme and the autumn fair. " +
        "A vote on the revised bylaws was postponed until every member had received the final draft. " +
        "The next meeting will take place in the town hall on the first Tuesday of the month.";

    private const string UNRELATED =
        "Mix the flour, sugar and butter until the dough comes together, then rest it in the fridge for an hour. " +
        "Roll it thin, cut rounds, and bake at two hundred degrees until the edges turn golden brown. " +
        "Let the biscuits cool on a rack before dusting them with icing sugar and serving with tea.";

    /// <summary>SHA-256 of the serialized signature of <see cref="ARTICLE"/>; produced on win-x64, re-checked on every platform.</summary>
    private const string GOLDEN_SIGNATURE_SHA256 = "31bc754a13b79c3c397082e27d8219fd296d3845944f519091ffced230df6792";

    #endregion

    #region Tests

    [Test]
    public void SignatureIsPinnedAndRepeatableTest()
    {
        var first = MinHashSigner.Sign(ARTICLE);
        var second = MinHashSigner.Sign(ARTICLE);
        var digest = Convert.ToHexStringLower(SHA256.HashData(MinHashSigner.ToBytes(first)));

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Length.EqualTo(MinHashSigner.HASHES));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.All(slot => slot != MinHashSigner.EMPTY_SLOT), Is.True);
            Assert.That(digest, Is.EqualTo(GOLDEN_SIGNATURE_SHA256), $"the signature digest is {digest}");
        });
    }

    [Test]
    public void NearDuplicatesScoreHighAndUnrelatedTextsLowTest()
    {
        var article = MinHashSigner.Sign(ARTICLE);
        var near = MinHashSigner.Sign(NEAR_DUPLICATE);
        var unrelated = MinHashSigner.Sign(UNRELATED);

        var nearScore = MinHashSigner.Similarity(article, near);
        var unrelatedScore = MinHashSigner.Similarity(article, unrelated);
        TestContext.Out.WriteLine($"near-duplicate {nearScore:F3}, unrelated {unrelatedScore:F3}");

        Assert.Multiple(() =>
        {
            Assert.That(MinHashSigner.Similarity(article, article), Is.EqualTo(1.0));
            Assert.That(nearScore, Is.GreaterThan(0.75), "one changed word in five sentences");
            Assert.That(unrelatedScore, Is.LessThan(0.15));
        });
    }

    [Test]
    public void BandKeysCollideForNearDuplicatesOnlyTest()
    {
        const int bands = 16;
        var article = MinHashSigner.BandKeys(MinHashSigner.Sign(ARTICLE), bands);
        var near = MinHashSigner.BandKeys(MinHashSigner.Sign(NEAR_DUPLICATE), bands);
        var unrelated = MinHashSigner.BandKeys(MinHashSigner.Sign(UNRELATED), bands);

        var nearCollisions = article.Zip(near).Count(pair => pair.First == pair.Second);
        var unrelatedCollisions = article.Zip(unrelated).Count(pair => pair.First == pair.Second);

        Assert.Multiple(() =>
        {
            Assert.That(nearCollisions, Is.GreaterThan(0), "a near-duplicate shares at least one band");
            Assert.That(unrelatedCollisions, Is.Zero);
            Assert.Throws<ArgumentException>(() => MinHashSigner.BandKeys(article, 7));
        });
    }

    [Test]
    public void ShinglesAreCaseAndPunctuationBlindAndShortTextHasNoneTest()
    {
        var a = MinHashSigner.Shingles("The Committee reviewed the annual report, in detail!");
        var b = MinHashSigner.Shingles("the committee reviewed the annual report in detail");

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a[0], Is.EqualTo("the committee reviewed the annual"));
            Assert.That(MinHashSigner.Shingles("too short"), Is.Empty);
            Assert.That(MinHashSigner.Sign("too short").All(slot => slot == MinHashSigner.EMPTY_SLOT), Is.True);
        });
    }

    #endregion
}
