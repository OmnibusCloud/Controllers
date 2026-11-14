using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Tests.Storage;

/// <summary>
/// The storage estimate under the output mode and the MinHash parameters: statistics and hashes
/// only leaves the report, the integrity document and the inputs (no text, no signatures, no
/// second copies, no dedup document) and the preflight line says so; a longer signature costs its
/// bytes; the defaults estimate exactly as before.
/// </summary>
[TestFixture]
public sealed class CorpusStorageOutputTests
{
    #region Constants

    private const long SHARD_BYTES = 64L * 1024 * 1024;

    #endregion

    #region Tests

    [Test]
    public void StatsAndHashesLeavesOnlyTheDocumentsAndTheInputsTest()
    {
        var full = Estimate(new CorpusPipelineData { Version = "v" });
        var stats = Estimate(new CorpusPipelineData { Version = "v", Output = CorpusOutputMode.StatsAndHashes });

        Assert.Multiple(() =>
        {
            Assert.That(stats.TextBytes, Is.Zero);
            Assert.That(stats.SignatureBytes, Is.Zero);
            Assert.That(stats.ReplicaBytes, Is.Zero, "a second execution keeps nothing either");
            Assert.That(stats.ProductsKept, Is.False);
            Assert.That(full.ProductsKept, Is.True);
            Assert.That(stats.InputBytes, Is.EqualTo(full.InputBytes));
            Assert.That(stats.DocumentBytes, Is.EqualTo(
                CorpusStorageRatios.REPORT_BASE_BYTES + 4L * CorpusStorageRatios.REPORT_BYTES_PER_SHARD
                + CorpusStorageRatios.INTEGRITY_BASE_BYTES + 1L * CorpusStorageRatios.INTEGRITY_BYTES_PER_PAIR), "the report and the integrity document, no dedup document");
            Assert.That(stats.TotalBytes, Is.LessThan(full.TotalBytes / 100), "four 64 MiB shards: kilobytes instead of about 100 MiB");
            Assert.That(stats.Documents, Is.EqualTo(full.Documents), "the documents made on the nodes are counted all the same");
        });
    }

    [Test]
    public void ThePreflightLineSaysTheProductsStayOnTheNodesTest()
    {
        var line = CorpusStorageCheck.Describe(Estimate(new CorpusPipelineData { Version = "v", Output = CorpusOutputMode.StatsAndHashes }), CorpusStorageLimit.NONE);

        Assert.Multiple(() =>
        {
            Assert.That(line, Does.Contain("no text and no signatures (output stats_and_hashes: they stay on the nodes)"));
            Assert.That(line, Does.Not.Contain("text 0 bytes"));
        });
    }

    [Test]
    public void ALongerSignatureCostsItsBytesTest()
    {
        var standard = Estimate(new CorpusPipelineData { Version = "v" });
        var explicitDefaults = Estimate(new CorpusPipelineData { Version = "v", MinHash = new CorpusMinHashData { Hashes = 128, ShingleWords = 5, Bands = 16 } });
        var longer = Estimate(new CorpusPipelineData { Version = "v", MinHash = new CorpusMinHashData { Hashes = 256 } });

        Assert.Multiple(() =>
        {
            Assert.That(explicitDefaults.Is(standard), Is.True, "the defaults written out estimate as before");
            Assert.That(standard.SignatureBytes, Is.EqualTo(4 * (12 + standard.Documents / 4 * 1024)));
            Assert.That(longer.SignatureBytes, Is.EqualTo(4 * (16 + longer.Documents / 4 * 2048)), "a WITSIG2 header and 2 KiB a document");
            Assert.That(longer.TextBytes, Is.EqualTo(standard.TextBytes));
        });
    }

    #endregion

    #region Tools

    private static CorpusStorageEstimate Estimate(CorpusPipelineData pipeline)
    {
        var sources = Enumerable.Range(0, 4)
            .Select(index => new CorpusSourceData { Url = $"https://data.commoncrawl.org/{index}.warc.wet.gz", Format = CorpusSourceFormat.Wet, SizeBytes = SHARD_BYTES })
            .ToList();

        return CorpusStorageEstimator.Estimate(sources, pipeline, 0.25, 700_000);
    }

    #endregion
}
