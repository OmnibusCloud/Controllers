using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Manifests;

/// <summary>
/// The traffic promise. The fetches: every shard once, and the packages sampled for integrity a
/// second time, the largest taken as the sampled ones (as the storage estimate takes them). Audit
/// finding M-8: the figure was the first fetch of each shard and nothing else; now it counts what
/// the contributors upload, the language model each node downloads, and an allowance for what is
/// fetched more than once, and says in its own words what no allowance covers. Audit finding H-8:
/// beside the estimate stands what the nodes enforce.
/// </summary>
[TestFixture]
public sealed class CorpusTrafficEstimateTests
{
    #region Fetch Tests

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
            Assert.That(none.FetchBytes, Is.EqualTo(1_000));
            Assert.That(none.SampledShards, Is.Zero);
            Assert.That(half.CopyBytes, Is.EqualTo(700), "the two largest: 400 and 300");
            Assert.That(half.FetchBytes, Is.EqualTo(1_700));
            Assert.That(some.SampledShards, Is.EqualTo(1), "a share above 0 samples at least one shard, as the platform does");
            Assert.That(some.CopyBytes, Is.EqualTo(400));
            Assert.That(all.FetchBytes, Is.EqualTo(2_000));
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
            Assert.That(sampled.Notes, Has.Some.EqualTo("2 shard(s), cap 1073741824 bytes each, fetches by the manifest's own sizes 7000 bytes (3000 of them the second fetch of the 1 largest shard(s), sampled for integrity)."));
        });
    }

    [Test]
    public void M5PastTheMostPackagesARoundHasTheSampledUnitIsAPackageTest()
    {
        // 3,000 shards travel in 1,000 packages of three: the platform samples packages, so the
        // second fetch is of whole packages, the largest taken.
        var sizes = Enumerable.Range(0, 3000).Select(index => (long)(index < 3 ? 1_000_000 : 1_000)).ToList();

        var estimate = CorpusTrafficEstimate.Of(sizes, 0.001);

        Assert.Multiple(() =>
        {
            Assert.That(CorpusPackagePlanner.ShardsPerPackage(sizes.Count), Is.EqualTo(3));
            Assert.That(estimate.SampledPackages, Is.EqualTo(1));
            Assert.That(estimate.SampledShards, Is.EqualTo(3));
            Assert.That(estimate.CopyBytes, Is.EqualTo(3_000_000), "the first package: the three largest shards together");
            Assert.That(estimate.Describe(), Does.Contain("the second fetch of the 1 largest package(s), 3 shard(s), sampled for integrity, 3000000"));
        });
    }

    #endregion

    #region M-8 Tests

    [Test]
    public void M8TheEstimateCountsTheUploadsTheModelAndAnAllowanceTest()
    {
        long[] sizes = [100, 400, 300, 200];

        var estimate = CorpusTrafficEstimate.Of(sizes, 0.5, null, uploadBytes: 600, modelBytes: 50, nodes: 3);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.FetchBytes, Is.EqualTo(1_700));
            Assert.That(estimate.UploadBytes, Is.EqualTo(600), "what every execution uploads");
            Assert.That(estimate.ModelBytes, Is.EqualTo(150), "the language model, once for each node that takes part");
            Assert.That(estimate.RetryBytes, Is.EqualTo(170), "a tenth of the fetches");
            Assert.That(estimate.TotalBytes, Is.EqualTo(1_700 + 600 + 150 + 170));
            Assert.That(CorpusTrafficEstimate.RETRY_ALLOWANCE, Is.EqualTo(0.1));
        });
    }

    [Test]
    public void M8TheAllowanceIsRoundedUpTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusTrafficEstimate.Of([1], 0).RetryBytes, Is.EqualTo(1));
            Assert.That(CorpusTrafficEstimate.Of([1_584_054_229], 0).RetryBytes, Is.EqualTo(158_405_423));
            Assert.That(CorpusTrafficEstimate.Of([], 0).TotalBytes, Is.Zero);
        });
    }

    [Test]
    public void M8TheLineSaysWhatItCountsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusTrafficEstimate.Of([100, 400, 300, 200], 0.5, null, 600, 50, 3).Describe(),
                Is.EqualTo("Contributor traffic estimate 2620 bytes: fetches 1700 (every shard once 1000 and the second fetch of the 2 largest shard(s), sampled for integrity, 700), uploads 600 (the products and the result packages of every execution), the language model 150 (3 node(s), once each) and an allowance of 170 (a tenth of the fetches) for fetches that start over and for shards fetched again in a repair or a dispute."));
            Assert.That(CorpusTrafficEstimate.Of([100, 400], 0).Describe(),
                Is.EqualTo("Contributor traffic estimate 550 bytes: fetches 500 (every shard once 500), uploads 0 (the products and the result packages of every execution) and an allowance of 50 (a tenth of the fetches) for fetches that start over and for shards fetched again in a repair or a dispute."));
        });
    }

    [Test]
    public void M8WhatNoAllowanceCoversIsSaidTest()
    {
        var sampled = CorpusTrafficEstimate.Of([100, 400, 300, 200], 0.5, null, 0, 50, 0).DescribeRounds();
        var plain = CorpusTrafficEstimate.Of([100, 400], 0).DescribeRounds();

        Assert.Multiple(() =>
        {
            Assert.That(sampled, Does.StartWith("Not in the traffic estimate: a node that fails or falls silent has its whole share fetched again by other nodes."));
            Assert.That(sampled, Does.Contain("The allowance is not a bound: a shard whose two copies disagree is fetched again by up to 3 machines, and by up to 3 more when they leave it unsettled (a dispute and its second round; 2 shard(s) are sampled here), and a shard a node returns as a fault of its own is fetched again by 2 machines (a repair); the report's counters (repairs, disputes) say what a run added."));
            Assert.That(sampled, Does.EndWith("The number of nodes is not known here: every node that takes part also downloads the language model once (50 bytes)."), "a model that could not be counted is named");
            Assert.That(plain, Does.Contain("The allowance is not a bound: a shard a node returns as a fault of its own is fetched once more (a repair); the report's counters (repairs, disputes) say what a run added."), "a run that samples nothing repairs on one machine");
            Assert.That(plain, Does.Not.Contain("(a dispute"), "nothing is sampled: nothing can be disputed");
            Assert.That(plain, Does.Not.Contain("language model"), "the job has none");
        });
    }

    #endregion

    #region H-8 Tests

    [Test]
    public void H8TheCeilingIsWhatTheNodesHoldEveryShardToTest()
    {
        // Two shards declared at 10 MiB and 100 MiB, one with no size of its own under a cap of 1 GiB.
        long[] sizes = [10L << 20, 100L << 20, 64L << 20];
        long[] bounds = [ShardLimits.DeclaredCap(10L << 20), ShardLimits.DeclaredCap(100L << 20), 1L << 30];

        var estimate = CorpusTrafficEstimate.Of(sizes, 0.34, bounds, 0, 0, 0);
        var line = estimate.DescribeCeiling(1, 1L << 30);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.SampledPackages, Is.EqualTo(2));
            Assert.That(estimate.CeilingBytes, Is.EqualTo(bounds.Sum() + (1L << 30) + ShardLimits.DeclaredCap(100L << 20)), "every shard at its bound, and the two largest bounds a second time");
            Assert.That(estimate.CeilingBytes, Is.GreaterThan(estimate.FetchBytes));
            Assert.That(line, Does.StartWith($"The nodes accept at most {estimate.CeilingBytes} bytes for those fetches, whoever wrote the manifest"));
            Assert.That(line, Does.Contain("1/8 more, 1048576 bytes at least").And.Contain("oversized_declared"));
            Assert.That(line, Does.Contain("1 shard(s) have no size of their own and are held to the cap of 1073741824 bytes only"));
            Assert.That(CorpusTrafficEstimate.Of(sizes, 0, bounds[..2].Append(ShardLimits.DeclaredCap(64L << 20)).ToList(), 0, 0, 0).DescribeCeiling(0, 1L << 30), Does.Not.Contain("no size of their own"));
        });
    }

    #endregion
}
