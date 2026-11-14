using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Tests.Dedup;

/// <summary>
/// What the near-duplicate merge reads from a report, and what it refuses: a run that kept
/// statistics and hashes only, a run that did not sign, shards whose signatures were made with
/// different MinHash parameters, a blob that says otherwise than the report. A report without the
/// parameters (Corpus 0.2.x) reads as the defaults; the merge uses the run's band layout.
/// </summary>
[TestFixture]
public sealed class CorpusDedupInputTests
{
    #region Tests

    [Test]
    public void AFullRunReadsWithItsParametersTest()
    {
        var minHash = new CorpusMinHashData { Hashes = 64, ShingleWords = 5, Bands = 8 };
        var input = CorpusDedupInput.Read(Report(Shard(0, minHash), Shard(1, minHash), Refused(2)));

        Assert.Multiple(() =>
        {
            Assert.That(input.Refusal, Is.Null);
            Assert.That(input.Shards.Select(shard => shard.Index), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(input.Parameters, Is.EqualTo(new MinHashParameters(64, 5, 8)));
        });
    }

    [Test]
    public void AReportWithoutParametersReadsAsTheDefaultsTest()
    {
        var report = Report(Shard(0, null), Shard(1, null));

        var input = CorpusDedupInput.Read(report);

        Assert.Multiple(() =>
        {
            Assert.That(input.Refusal, Is.Null);
            Assert.That(input.Parameters, Is.EqualTo(MinHashParameters.DEFAULT));
            Assert.That(input.Shards, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void AStatsAndHashesRunIsRefusedTest()
    {
        var result = Shard(0, null);
        result.TextBlobId = Guid.Empty;
        result.SignaturesBlobId = Guid.Empty;
        result.Output = CorpusOutputMode.StatsAndHashes;

        var input = CorpusDedupInput.Read(Report(result));

        Assert.That(input.Refusal, Does.StartWith("The run kept statistics and hashes only (pipeline output \"stats_and_hashes\"): it has no signatures"));
    }

    [Test]
    public void ARunWithoutSignaturesIsRefusedTest()
    {
        var unsigned = Shard(3, null);
        unsigned.SignaturesBlobId = Guid.Empty;
        var empty = Shard(4, null);
        empty.SignaturesBlobId = Guid.Empty;
        empty.Stats!.DocumentsOut = 0;

        var refused = CorpusDedupInput.Read(Report(Shard(0, null), unsigned));
        var nothingToMerge = CorpusDedupInput.Read(Report(Shard(0, null), empty));

        Assert.Multiple(() =>
        {
            Assert.That(refused.Refusal, Does.StartWith("Shard 3 completed with documents and no signatures"));
            Assert.That(nothingToMerge.Refusal, Is.Null, "a shard with no document has nothing to sign");
            Assert.That(nothingToMerge.Shards, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void SignaturesMadeWithDifferentParametersAreRefusedTest()
    {
        var input = CorpusDedupInput.Read(Report(Shard(0, null), Shard(5, new CorpusMinHashData { Hashes = 256, ShingleWords = 5, Bands = 16 })));

        Assert.That(input.Refusal, Is.EqualTo(
            "The shards' signatures were made with different MinHash parameters (shard 0: 128 hashes over 5-word shingles, 16 bands of 8 rows; " +
            "shard 5: 256 hashes over 5-word shingles, 16 bands of 16 rows): signatures made differently cannot be compared."));
    }

    [Test]
    public void ABlobThatSaysOtherwiseIsRefusedTest()
    {
        var source = new CorpusDedupSource(2, Guid.NewGuid(), MinHashParameters.DEFAULT);
        using var other = new MemoryStream();
        ShardSignatureWriter.WriteHeader(other, new MinHashParameters(128, 7, 16));
        using var standard = new MemoryStream();
        ShardSignatureWriter.WriteHeader(standard);

        Assert.Multiple(() =>
        {
            Assert.That(CorpusDedupInput.Check(source, standard.ToArray()), Is.Null);
            Assert.That(CorpusDedupInput.Check(source, other.ToArray()), Is.EqualTo(
                "Shard 2's signatures blob holds 128 hashes over 7-word shingles, the report says 128 over 5: signatures made differently cannot be compared."));
        });
    }

    [Test]
    public void TheMergeUsesTheRunsBandsAndTheDocumentNamesTheParametersTest()
    {
        var texts = new[]
        {
            "The committee reviewed the annual report in detail and approved the budget for the coming year after a long debate.",
            "The committee reviewed the annual report in detail and approved the budget for the coming year after a short debate.",
            "Mix the flour, sugar and butter until the dough comes together, then rest it in the fridge for an hour before baking."
        };
        var parameters = new MinHashParameters(64, 3, 32);
        var signatures = texts.Select(text => MinHashSigner.Sign(text, parameters.Hashes, parameters.ShingleWords)).ToList();

        var result = new CorpusDeduplicator(0.5, parameters.Bands).Run([new CorpusDedupShard(0, Guid.NewGuid(), signatures)]);
        using var document = System.Text.Json.JsonDocument.Parse(CorpusDedupWriter.Write(result, parameters));
        var summary = document.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(result.Bands, Is.EqualTo(32));
            Assert.That(result.Duplicates, Is.EqualTo(1), "the two committee texts are one cluster");
            Assert.That(summary.GetProperty("bands").GetInt32(), Is.EqualTo(32));
            Assert.That(summary.GetProperty("hashes").GetInt32(), Is.EqualTo(64));
            Assert.That(summary.GetProperty("shingle_words").GetInt32(), Is.EqualTo(3));
            Assert.That(CorpusDeduplicator.Capacity(128), Is.EqualTo(CorpusDeduplicator.MAX_DOCUMENTS));
            Assert.That(CorpusDeduplicator.Capacity(1024), Is.EqualTo(CorpusDeduplicator.MAX_DOCUMENTS / 8));
        });
    }

    #endregion

    #region Tools

    private static byte[] Report(params CorpusResultData[] results)
    {
        return CorpusReportWriter.Write(results);
    }

    private static CorpusResultData Shard(int index, CorpusMinHashData? minHash)
    {
        return new CorpusResultData
        {
            TaskIndex = index,
            Verdict = CorpusShardVerdict.Completed,
            TextBlobId = Guid.NewGuid(),
            SignaturesBlobId = Guid.NewGuid(),
            ContentSha256 = new string('a', 64),
            ControllerVersion = "test",
            MinHash = minHash,
            Stats = new CorpusShardStatsData { RecordsIn = 10, DocumentsExtracted = 8, DocumentsOut = 5 }
        };
    }

    private static CorpusResultData Refused(int index)
    {
        return new CorpusResultData { TaskIndex = index, Verdict = CorpusShardVerdict.OriginRefused, Error = "not a public origin" };
    }

    #endregion
}
