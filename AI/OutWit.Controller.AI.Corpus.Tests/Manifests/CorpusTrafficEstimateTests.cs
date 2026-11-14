using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Tests.Manifests;

/// <summary>
/// The traffic promise: every shard once, and the shards sampled for integrity a second time, the
/// largest ones taken as the sampled ones (as the storage estimate takes them); the plan and the
/// preflight state it.
/// </summary>
[TestFixture]
public sealed class CorpusTrafficEstimateTests
{
    #region Tests

    [Test]
    public void TheSampledShardsAreFetchedTwiceTheLargestTakenTest()
    {
        long[] sizes = [100, 400, 300, 200];

        var none = CorpusTrafficEstimate.Of(sizes, 0);
        var half = CorpusTrafficEstimate.Of(sizes, 0.5);
        var some = CorpusTrafficEstimate.Of(sizes, 0.1);
        var all = CorpusTrafficEstimate.Of(sizes, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(none.TotalBytes, Is.EqualTo(1_000));
            Assert.That(none.SampledShards, Is.Zero);
            Assert.That(half.CopyBytes, Is.EqualTo(700), "the two largest: 400 and 300");
            Assert.That(half.TotalBytes, Is.EqualTo(1_700));
            Assert.That(some.SampledShards, Is.EqualTo(1), "a share above 0 samples at least one shard, as the platform does");
            Assert.That(some.CopyBytes, Is.EqualTo(400));
            Assert.That(all.TotalBytes, Is.EqualTo(2_000));
        });
    }

    [Test]
    public void AnUnknownSizeIsNotCountedTest()
    {
        var estimate = CorpusTrafficEstimate.Of([0, 500, -1], 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.OnceBytes, Is.EqualTo(500));
            Assert.That(estimate.CopyBytes, Is.EqualTo(500), "three shards sampled, one of them sized");
        });
    }

    [Test]
    public void TheLineSaysWhatItCountsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusTrafficEstimate.Of([100, 400, 300, 200], 0.5).Describe(),
                Is.EqualTo("Contributor traffic estimate 1700 bytes: every shard once (1000) and the second fetch of the 2 largest shard(s), sampled for integrity (700)."));
            Assert.That(CorpusTrafficEstimate.Of([100, 400], 0).Describe(), Is.EqualTo("Contributor traffic estimate 500 bytes: every shard once (500)."));
        });
    }

    [Test]
    public void ThePlanCountsTheCopiesTest()
    {
        var manifest = CorpusManifestParser.Parse("""
            {"pipeline":{"version":"v","identify_language":false},"allowed_origins":["data.commoncrawl.org"],
             "sources":[{"url":"https://data.commoncrawl.org/a.warc.wet.gz","size":1000},{"url":"https://data.commoncrawl.org/b.warc.wet.gz","size":3000}]}
            """);

        var once = CorpusTaskPlanner.Plan(manifest, null, Guid.Empty, "");
        var sampled = CorpusTaskPlanner.Plan(manifest, new CorpusOptionsData { SamplingRate = 0.5 }, Guid.Empty, "");

        Assert.Multiple(() =>
        {
            Assert.That(once.EstimatedTrafficBytes, Is.EqualTo(4_000));
            Assert.That(sampled.EstimatedTrafficBytes, Is.EqualTo(7_000), "the larger shard a second time");
            Assert.That(sampled.Notes, Has.Some.EqualTo("2 shard(s), cap 1073741824 bytes each, estimated traffic 7000 bytes (3000 of them the second fetch of the 1 largest shard(s), sampled for integrity)."));
        });
    }

    #endregion
}
