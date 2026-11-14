using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Tests.Storage;

/// <summary>
/// The server storage estimate: it holds what a real run left behind, counts the second copies
/// of the sampled shards by the platform's rule, follows the pipeline's switches, and says what
/// it could not count.
/// </summary>
[TestFixture]
public sealed class CorpusStorageEstimatorTests
{
    #region Constants

    private const long MIB = 1024 * 1024;

    /// <summary>
    /// The test fleet's acceptance run of 2026-11-14 (controller 0.1.13, twenty Common Crawl WET
    /// shards of CC-MAIN-2026-43, every stage on, sampling 0.25): what its report says it fetched
    /// and produced, per shard.
    /// </summary>
    private static readonly (long Fetched, long Text, int Documents)[] LIVE_RUN =
    [
        (66560928, 17564983, 10251), (64004647, 16769026, 10085), (67231062, 16669007, 10118), (65138541, 16605362, 10147),
        (69422286, 17753714, 10213), (67997951, 16742186, 10174), (64738679, 16409506, 10126), (66011273, 16804738, 10156),
        (67401960, 17031525, 10261), (66771602, 16922590, 10230), (63837350, 16748289, 10097), (66509193, 16510776, 10312),
        (65420431, 16805291, 10209), (69138015, 16522628, 10088), (67094705, 17338649, 10193), (67874161, 17165801, 10016),
        (66156505, 16855641, 10152), (64774502, 17286797, 10372), (64965241, 16445066, 10074), (65820129, 17228510, 10188)
    ];

    /// <summary>The shards that run sampled in that run.</summary>
    private static readonly int[] LIVE_SAMPLED = [1, 5, 8, 10, 17];

    /// <summary>Its report, integrity and dedup documents, and its inputs (manifest and model).</summary>
    private const long LIVE_DOCUMENTS = 39116 + 2745 + 1129619;
    private const long LIVE_INPUTS = 3314 + 241721;

    #endregion

    #region Measured Run Tests

    [Test]
    public void TheEstimateHoldsTheTwentyShardRunTest()
    {
        var sources = LIVE_RUN.Select(shard => Wet(shard.Fetched)).ToList();
        var estimate = CorpusStorageEstimator.Estimate(sources, new CorpusPipelineData(), 0.25, LIVE_INPUTS);

        var products = LIVE_RUN.Select(shard => shard.Text + Signatures(shard.Documents)).ToList();
        var actual = products.Sum() + LIVE_SAMPLED.Sum(index => products[index]) + LIVE_DOCUMENTS + LIVE_INPUTS;

        Assert.Multiple(() =>
        {
            Assert.That(estimate.TotalBytes, Is.GreaterThanOrEqualTo(actual), "the estimate must not fall short of a measured run");
            Assert.That(estimate.TotalBytes, Is.LessThan(actual * 1.5), "nor be so large that it refuses jobs that fit");
            Assert.That(estimate.TextBytes, Is.GreaterThanOrEqualTo(LIVE_RUN.Sum(shard => shard.Text)));
            Assert.That(estimate.Documents, Is.GreaterThanOrEqualTo(LIVE_RUN.Sum(shard => (long)shard.Documents)));
            Assert.That(estimate.SampledShards, Is.EqualTo(5));
            Assert.That(estimate.InputBytes, Is.EqualTo(LIVE_INPUTS));
            Assert.That(estimate.UnsizedSources, Is.Zero);
        });
    }

    #endregion

    #region Sampling Tests

    [TestCase(20, 0.25, 5)]
    [TestCase(20, 0.01, 1)]
    [TestCase(10, 0.1, 1)]
    [TestCase(3, 2.0, 3)]
    [TestCase(3, 0.0, 0)]
    [TestCase(3, -1.0, 0)]
    [TestCase(0, 0.5, 0)]
    public void SampledShardsFollowThePlatformRuleTest(int shards, double rate, int expected)
    {
        Assert.That(CorpusStorageEstimator.SampledShards(shards, rate), Is.EqualTo(expected));
    }

    [Test]
    public void TheLargestShardsAreTakenAsTheSampledOnesTest()
    {
        var sources = new List<CorpusSourceData> { Wet(10 * MIB), Wet(100 * MIB), Wet(50 * MIB) };
        var pipeline = new CorpusPipelineData();

        var once = CorpusStorageEstimator.Estimate(sources, pipeline, 0, 0);
        var sampled = CorpusStorageEstimator.Estimate(sources, pipeline, 0.3, 0);
        var largest = CorpusStorageEstimator.Estimate([Wet(100 * MIB)], pipeline, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(once.ReplicaBytes, Is.Zero);
            Assert.That(sampled.SampledShards, Is.EqualTo(1));
            Assert.That(sampled.ReplicaBytes, Is.EqualTo(largest.TextBytes + largest.SignatureBytes));
        });
    }

    #endregion

    #region Pipeline Tests

    [Test]
    public void APipelineThatDoesNotSignLeavesNoSignaturesAndNoDedupTest()
    {
        var sources = new List<CorpusSourceData> { Wet(64 * MIB) };

        var signed = CorpusStorageEstimator.Estimate(sources, new CorpusPipelineData(), 0, 0);
        var unsigned = CorpusStorageEstimator.Estimate(sources, new CorpusPipelineData { Sign = false }, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(signed.SignatureBytes, Is.EqualTo(CorpusStorageRatios.SIGNATURE_HEADER_BYTES + signed.Documents * CorpusStorageRatios.SIGNATURE_BYTES_PER_DOCUMENT));
            Assert.That(unsigned.SignatureBytes, Is.Zero);
            Assert.That(unsigned.TextBytes, Is.EqualTo(signed.TextBytes));
            Assert.That(unsigned.DocumentBytes, Is.LessThan(signed.DocumentBytes), "no signatures, no dedup document");
        });
    }

    [Test]
    public void TurningAFilterOffMakesTheEstimateLargerTest()
    {
        var sources = new List<CorpusSourceData> { Wet(64 * MIB), Warc(1024 * MIB) };

        var full = CorpusStorageEstimator.Estimate(sources, new CorpusPipelineData(), 0, 0).TotalBytes;
        var noQuality = CorpusStorageEstimator.Estimate(sources, new CorpusPipelineData { FilterQuality = false }, 0, 0).TotalBytes;
        var noLines = CorpusStorageEstimator.Estimate(sources, new CorpusPipelineData { CleanLines = false }, 0, 0).TotalBytes;
        var neither = CorpusStorageEstimator.Estimate(sources, new CorpusPipelineData { FilterQuality = false, CleanLines = false }, 0, 0).TotalBytes;

        Assert.Multiple(() =>
        {
            Assert.That(noQuality, Is.GreaterThan(full));
            Assert.That(noLines, Is.GreaterThan(full));
            Assert.That(neither, Is.GreaterThan(noQuality));
            Assert.That(neither, Is.GreaterThan(noLines));
        });
    }

    [Test]
    public void AWarcByteLeavesLessThanAWetByteTest()
    {
        var wet = CorpusStorageEstimator.Estimate([Wet(1024 * MIB)], new CorpusPipelineData(), 0, 0);
        var warc = CorpusStorageEstimator.Estimate([Warc(1024 * MIB)], new CorpusPipelineData(), 0, 0);

        Assert.That(warc.TextBytes + warc.SignatureBytes, Is.LessThan(wet.TextBytes + wet.SignatureBytes), "a WARC file is mostly markup and records that are not pages");
    }

    #endregion

    #region Edge Tests

    [Test]
    public void SourcesWithoutASizeAreCountedAndLeftOutTest()
    {
        var sources = new List<CorpusSourceData> { Wet(64 * MIB), new() { Url = "https://data.commoncrawl.org/x.warc.wet.gz", Format = CorpusSourceFormat.Wet } };

        var estimate = CorpusStorageEstimator.Estimate(sources, new CorpusPipelineData(), 0, 0);
        var sizedOnly = CorpusStorageEstimator.Estimate([Wet(64 * MIB)], new CorpusPipelineData(), 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.UnsizedSources, Is.EqualTo(1));
            Assert.That(estimate.TextBytes, Is.EqualTo(sizedOnly.TextBytes));
            Assert.That(estimate.SignatureBytes, Is.EqualTo(sizedOnly.SignatureBytes));
        });
    }

    [Test]
    public void AnEmptyJobLeavesItsInputsOnlyTest()
    {
        var estimate = CorpusStorageEstimator.Estimate([], new CorpusPipelineData(), 0.5, 1234);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.TotalBytes, Is.EqualTo(1234));
            Assert.That(estimate.SampledShards, Is.Zero);
        });
    }

    #endregion

    #region Tools

    private static CorpusSourceData Wet(long size)
    {
        return new CorpusSourceData { Url = "https://data.commoncrawl.org/x.warc.wet.gz", Format = CorpusSourceFormat.Wet, SizeBytes = size };
    }

    private static CorpusSourceData Warc(long size)
    {
        return new CorpusSourceData { Url = "https://data.commoncrawl.org/x.warc.gz", Format = CorpusSourceFormat.Warc, SizeBytes = size };
    }

    private static long Signatures(int documents)
    {
        return CorpusStorageRatios.SIGNATURE_HEADER_BYTES + (long)documents * CorpusStorageRatios.SIGNATURE_BYTES_PER_DOCUMENT;
    }

    #endregion
}
