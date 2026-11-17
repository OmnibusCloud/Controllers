using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Dedup;

/// <summary>
/// What the near-duplicate merge reads from a report, and what it refuses: a run that kept
/// statistics and hashes only, a run that did not sign, a report that names one blob or one shard
/// twice, more documents than a merge holds (audit finding H-1: refused from the report's own
/// counts, before a blob is opened), anything that is not a report. A shard whose signatures were
/// made with other parameters is left out, not the run (H-3). A report without the parameters
/// (Corpus 0.2.x) reads as the defaults; the merge uses the run's band layout.
/// </summary>
[TestFixture]
public sealed class CorpusDedupInputTests
{
    #region Tests

    [Test]
    public void AFullRunReadsWithItsParametersTest()
    {
        var pipeline = CorpusTestRun.Signed();
        pipeline.MinHash = new CorpusMinHashData { Hashes = 64, ShingleWords = 5, Bands = 8 };
        var tasks = CorpusTestRun.Tasks(3, pipeline);
        var completed = CorpusTestRun.Completed(tasks[1], documentsOut: 7);

        var input = CorpusDedupInput.Read(Report(tasks, CorpusTestRun.Completed(tasks[0]), completed, CorpusTestRun.Verdict(tasks[2], CorpusShardVerdict.OriginRefused, "origin_not_listed")));

        Assert.Multiple(() =>
        {
            Assert.That(input.Refusal, Is.Null);
            Assert.That(input.Shards.Select(shard => shard.Index), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(input.Parameters, Is.EqualTo(new MinHashParameters(64, 5, 8)));
            Assert.That(input.Shards[1].SignaturesBlobId, Is.EqualTo(completed.SignaturesBlobId));
            Assert.That(input.Shards[1].SignaturesSha256, Is.EqualTo(completed.SignaturesSha256), "what integrity compared travels to the merge");
            Assert.That(input.Shards[1].Documents, Is.EqualTo(7));
            Assert.That(input.Shards[1].ExpectedBytes, Is.EqualTo(16 + 7 * 64 * 8), "a header of 16 bytes (not the default parameters) and one signature per document");
        });
    }

    [Test]
    public void AReportWithoutParametersReadsAsTheDefaultsTest()
    {
        // A report of Corpus 0.2.x: no minhash, no signatures_sha256.
        var report = Edit(Report(2), shard =>
        {
            shard.Remove("minhash");
            shard.Remove("signatures_sha256");
        }, schema: "corpus-report/2");

        var input = CorpusDedupInput.Read(report);

        Assert.Multiple(() =>
        {
            Assert.That(input.Refusal, Is.Null);
            Assert.That(input.Parameters, Is.EqualTo(MinHashParameters.DEFAULT));
            Assert.That(input.Shards, Has.Count.EqualTo(2));
            Assert.That(input.Shards.Select(shard => shard.SignaturesSha256), Is.All.Empty, "no hash to hold the blob to: its length and header only");
        });
    }

    [Test]
    public void AStatsAndHashesRunIsRefusedTest()
    {
        var pipeline = CorpusTestRun.Signed();
        pipeline.Output = CorpusOutputMode.StatsAndHashes;
        var tasks = CorpusTestRun.Tasks(1, pipeline);

        var input = CorpusDedupInput.Read(Report(tasks, CorpusTestRun.Completed(tasks[0])));

        Assert.That(input.Refusal, Does.StartWith("The run kept statistics and hashes only (pipeline output \"stats_and_hashes\"): it has no signatures"));
    }

    [Test]
    public void ARunWithoutSignaturesIsRefusedTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        var refused = CorpusDedupInput.Read(Report(tasks, CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[1])));
        var nothingToMerge = CorpusDedupInput.Read(Report(tasks, CorpusTestRun.Completed(tasks[0], documentsOut: 0), CorpusTestRun.Completed(tasks[1], documentsOut: 0)));

        Assert.Multiple(() =>
        {
            Assert.That(refused.Refusal, Does.StartWith("Shard 0 completed with documents and no signatures"));
            Assert.That(nothingToMerge.Refusal, Is.Null, "a shard with no document has nothing to sign");
            Assert.That(nothingToMerge.Shards, Is.Empty);
        });
    }

    [Test]
    public void H3AShardSignedWithOtherParametersIsLeftOutNotTheRunTest()
    {
        var report = Edit(Report(3), shard =>
        {
            if (shard["index"]!.GetValue<int>() == 1)
                shard["minhash"] = new JsonObject { ["hashes"] = 256, ["shingle_words"] = 5, ["bands"] = 16 };
        });

        var input = CorpusDedupInput.Read(report);

        Assert.Multiple(() =>
        {
            Assert.That(input.Refusal, Is.Null);
            Assert.That(input.Parameters, Is.EqualTo(MinHashParameters.DEFAULT), "the run's parameters are those of most of its shards");
            Assert.That(input.Shards.Select(shard => shard.Index), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(input.Excluded.Select(exclusion => (exclusion.Index, exclusion.Reason)), Is.EqualTo(new[] { (1, CorpusDedupExclusion.PARAMETERS) }));
        });
    }

    [Test]
    public void H1AReportThatNamesOneBlobManyTimesIsRefusedTest()
    {
        // The audit's hostile report: a few kilobytes that name one real signatures blob ten
        // thousand times, each read whole into the server's memory.
        var blob = Guid.NewGuid().ToString("D");
        var index = 0;
        var report = Edit(Report(3), shard =>
        {
            shard["signatures_blob"] = blob;
            shard["index"] = index++;
        });

        var input = CorpusDedupInput.Read(report);

        Assert.Multiple(() =>
        {
            Assert.That(input.Refusal, Is.EqualTo("The report names the signatures blob of shard 1 more than once: every shard has a blob of its own."));
            Assert.That(input.Shards, Is.Empty);
        });
    }

    [Test]
    public void H1AReportThatListsAShardTwiceIsRefusedTest()
    {
        var report = Edit(Report(2), shard => shard["index"] = 0);

        Assert.That(CorpusDedupInput.Read(report).Refusal, Is.EqualTo("The report lists shard 0 more than once."));
    }

    [Test]
    public void H1MoreDocumentsThanAMergeHoldsAreRefusedFromTheReportTest()
    {
        // 1,000 shards of 12,000 documents: 12 million signatures. Refused here, from the counts
        // the report states; the audit's reading had every blob loaded first.
        var tasks = CorpusTestRun.Tasks(1000, CorpusTestRun.Signed());
        var report = Report(tasks, tasks.Select(task => CorpusTestRun.Completed(task, documentsOut: 12_000)).ToArray());

        var input = CorpusDedupInput.Read(report);

        Assert.Multiple(() =>
        {
            Assert.That(input.Refusal, Does.StartWith("The run has 12000000 documents and one merge holds 4000000 with 128-hash signatures"));
            Assert.That(input.Shards, Is.Empty, "nothing to open");
        });
    }

    [Test]
    public void WhatIsNotAReportIsRefusedByNameTest()
    {
        string[] hostile =
        [
            "", "null", "[]", "{}", "{\"schema\":\"corpus-report/4\"}", "{\"schema\":\"corpus-report/4\",\"shards\":7}",
            "{\"schema\":7,\"shards\":[]}", "{\"schema\":\"something-else/1\",\"shards\":[]}", "not json at all", "{\"schema\":\"corpus-report/4\",\"shards\":[",
            "{\"schema\":\"corpus-report/4\",\"shards\":[],\"shards\":[]}"
        ];

        Assert.Multiple(() =>
        {
            foreach (var text in hostile)
                Assert.That(() => CorpusDedupInput.Read(Encoding.UTF8.GetBytes(text)), Throws.InvalidOperationException.With.Message.EqualTo("The report is not a corpus-report/4 document."), text);
        });
    }

    [Test]
    public void AReportWithShardsOfAnotherShapeIsRefusedNotThrownTest()
    {
        (string Name, Action<JsonObject> Bend, string Refusal)[] cases =
        [
            ("an index that is a string", shard => shard["index"] = "0", "The report lists a completed shard without an index."),
            ("a negative index", shard => shard["index"] = -1, "The report lists a completed shard without an index."),
            ("no statistics", shard => shard["stats"] = null, "Shard 0 completed and its statistics state no document count."),
            ("a negative document count", shard => shard["stats"]!["documents_out"] = -5, "Shard 0 completed and its statistics state no document count."),
            ("a hash that is none", shard => shard["signatures_sha256"] = "abc", "Shard 0: 'signatures_sha256' is not a SHA-256."),
            ("a hash that is a number", shard => shard["signatures_sha256"] = 5, "Shard 0: 'signatures_sha256' is not a SHA-256."),
            ("parameters nobody can use", shard => shard["minhash"] = new JsonObject { ["hashes"] = 100000, ["shingle_words"] = 5, ["bands"] = 16 }, "Shard 0: the MinHash parameters the report states cannot be used.")
        ];

        Assert.Multiple(() =>
        {
            foreach (var (name, bend, refusal) in cases)
                Assert.That(CorpusDedupInput.Read(Edit(Report(1), bend)).Refusal, Is.EqualTo(refusal), name);

            var notAnObject = JsonNode.Parse(Report(1))!.AsObject();
            notAnObject["shards"] = new JsonArray(7);
            Assert.That(CorpusDedupInput.Read(Encoding.UTF8.GetBytes(notAnObject.ToJsonString())).Refusal, Is.EqualTo("The report lists a shard that is not an object."));
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

        var result = new CorpusDeduplicator(0.5, parameters.Bands).Run([CorpusDedupShard.Of(0, Guid.NewGuid(), signatures)]);
        using var document = JsonDocument.Parse(CorpusDedupWriter.Write(result, parameters));
        var summary = document.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(result.Bands, Is.EqualTo(32));
            Assert.That(result.Duplicates, Is.EqualTo(1), "the two committee texts are one cluster");
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo("corpus-dedup/2"));
            Assert.That(summary.GetProperty("bands").GetInt32(), Is.EqualTo(32));
            Assert.That(summary.GetProperty("hashes").GetInt32(), Is.EqualTo(64));
            Assert.That(summary.GetProperty("shingle_words").GetInt32(), Is.EqualTo(3));
            Assert.That(summary.GetProperty("excluded").GetInt32(), Is.Zero);
            Assert.That(CorpusDeduplicator.Capacity(128), Is.EqualTo(CorpusDeduplicator.MAX_DOCUMENTS));
            Assert.That(CorpusDeduplicator.Capacity(1024), Is.EqualTo(CorpusDeduplicator.MAX_DOCUMENTS / 8));
        });
    }

    #endregion

    #region Tools

    /// <summary>The report of a run of signing shards, each completed once.</summary>
    private static byte[] Report(int shards)
    {
        var tasks = CorpusTestRun.Tasks(shards, CorpusTestRun.Signed());
        return Report(tasks, tasks.Select(task => CorpusTestRun.Completed(task)).ToArray());
    }

    private static byte[] Report(IReadOnlyList<CorpusTaskData> tasks, params CorpusResultData[] results)
    {
        return CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, results));
    }

    /// <summary>A report with every shard entry changed as a lab (or a file gone wrong) could change it.</summary>
    private static byte[] Edit(byte[] report, Action<JsonObject> shard, string? schema = null)
    {
        var root = JsonNode.Parse(report)!.AsObject();
        if (schema != null)
            root["schema"] = schema;

        foreach (var entry in root["shards"]!.AsArray())
            shard(entry!.AsObject());

        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    #endregion
}
