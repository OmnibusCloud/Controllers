using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Docs;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Reports;

/// <summary>
/// The integrity document (<c>corpus-integrity/6</c>): the first round's pairs compared field by
/// field, signatures a mismatch of their own kind, a pair with a side that is no result counted
/// apart, the disputes and what settled them, the shards without a result, the values dropped; no
/// machine named.
/// </summary>
[TestFixture]
public sealed class CorpusIntegrityWriterTests
{
    #region Pair Tests

    [Test]
    public void PairsAreComparedByShardTest()
    {
        var tasks = CorpusTestRun.Tasks(4);
        var executions = new object?[]
        {
            CorpusTestRun.Completed(tasks[0], 'a', 10),
            CorpusTestRun.Completed(tasks[2], 'c', 30),
            CorpusTestRun.Completed(tasks[3], 'd', 40),
            CorpusTestRun.Completed(tasks[2], 'c', 30),
            CorpusTestRun.Completed(tasks[0], 'a', 11)
        };

        using var document = JsonDocument.Parse(CorpusIntegrityWriter.Write(CorpusTestRun.Ledger(tasks, executions)));
        var summary = document.RootElement.GetProperty("summary");
        var pairs = document.RootElement.GetProperty("pairs").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo("corpus-integrity/6"));
            Assert.That(summary.GetProperty("shards").GetInt32(), Is.EqualTo(4));
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(2), "the shard executed once makes no pair");
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.EqualTo(0.5));
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.EqualTo(1), "a mismatch is a dispute");
            Assert.That(summary.GetProperty("single_execution").GetInt32(), Is.EqualTo(1), "shard 3 ran once; shard 1 never came back");
            Assert.That(summary.GetProperty("no_result").GetInt32(), Is.EqualTo(1));

            Assert.That(pairs.Select(p => p.GetProperty("index").GetInt32()), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(pairs[0].GetProperty("compared").GetBoolean(), Is.True);
            Assert.That(pairs[0].GetProperty("match").GetBoolean(), Is.False);
            Assert.That(pairs[0].GetProperty("differs").EnumerateArray().Select(d => d.GetString()), Is.EqualTo(new[] { "documents", "languages" }));
            Assert.That(pairs[0].GetProperty("dispute").GetBoolean(), Is.True);
            Assert.That(pairs[1].GetProperty("match").GetBoolean(), Is.True);
            Assert.That(pairs[1].TryGetProperty("dispute", out _), Is.False);
        });
    }

    [TestCase(CorpusShardVerdict.FetchFailed, "http_503")]
    [TestCase(CorpusShardVerdict.OriginRefused, "origin_redirect")]
    [TestCase(CorpusShardVerdict.Oversized, "oversized")]
    [TestCase(CorpusShardVerdict.Malformed, "not_warc")]
    public void AnyOtherVerdictAgainstACompletedOneIsAMismatchTest(CorpusShardVerdict verdict, string code)
    {
        // A verdict is a statement about the shard: two machines that say different things of it
        // disagree, whichever of them is right.
        var tasks = CorpusTestRun.Tasks(1);
        using var document = JsonDocument.Parse(CorpusIntegrityWriter.Write(CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Verdict(tasks[0], verdict, code)])));
        var summary = document.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("incomplete").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("pairs")[0].GetProperty("differs").EnumerateArray().First().GetString(), Is.EqualTo("verdict"));
        });
    }

    [Test]
    public void APairWithASideThatIsNoResultIsIncompleteNotAMismatchTest()
    {
        // A node's own trouble says nothing about the shard: the pair is counted apart.
        var tasks = CorpusTestRun.Tasks(2);
        using var document = JsonDocument.Parse(CorpusIntegrityWriter.Write(CorpusTestRun.Ledger(tasks,
        [
            CorpusTestRun.Completed(tasks[0], 'e', 50), CorpusTestRun.Fault(tasks[0], CorpusResultCodes.FAULT_LANGUAGE_MODEL),
            CorpusTestRun.Completed(tasks[1], 'f', 60), CorpusTestRun.Completed(tasks[1], 'f', 60)
        ])));
        var summary = document.RootElement.GetProperty("summary");
        var pairs = document.RootElement.GetProperty("pairs").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("incomplete").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.Zero);
            Assert.That(summary.GetProperty("node_faults").GetInt32(), Is.EqualTo(1));
            Assert.That(pairs[0].GetProperty("compared").GetBoolean(), Is.False);
            Assert.That(pairs[0].GetProperty("incomplete").GetBoolean(), Is.True);
            Assert.That(pairs[0].TryGetProperty("match", out _), Is.False);
            Assert.That(pairs[0].GetProperty("replica").GetProperty("kind").GetString(), Is.EqualTo("fault"));
            Assert.That(pairs[0].GetProperty("replica").GetProperty("code").GetString(), Is.EqualTo("language_model"));
            Assert.That(pairs[1].TryGetProperty("incomplete", out _), Is.False);
            Assert.That(pairs[1].GetProperty("match").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void TheDocumentNamesNoMachineTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var first = CorpusTestRun.Completed(tasks[0]);
        first.NodeId = Guid.NewGuid();
        first.NodeName = "contributors-laptop";

        var json = Encoding.UTF8.GetString(CorpusIntegrityWriter.Write(CorpusTestRun.Ledger(tasks, [first, CorpusTestRun.Completed(tasks[0])])));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("contributors-laptop"));
            Assert.That(json, Does.Not.Contain(first.NodeId.ToString()));
            Assert.That(json, Does.Not.Contain("\"node\""));
            Assert.That(json, Does.Not.Contain("\"node_id"));
        });
    }

    [Test]
    public void ShardsExecutedOnceAreAnEmptyListOfPairsTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        using var document = JsonDocument.Parse(CorpusIntegrityWriter.Write(CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[1], 'b')])));

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("sampled").GetInt32(), Is.Zero);
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("single_execution").GetInt32(), Is.EqualTo(2), "and the document says every shard is one machine's word");
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("mismatch_rate").GetDouble(), Is.Zero);
            Assert.That(document.RootElement.GetProperty("pairs").GetArrayLength(), Is.Zero);
            Assert.That(document.RootElement.GetProperty("disputes").GetArrayLength(), Is.Zero);
            Assert.That(document.RootElement.GetProperty("no_result").GetArrayLength(), Is.Zero);
        });
    }

    #endregion

    #region Difference Tests

    [Test]
    public void DifferencesNameEveryFieldTest()
    {
        var task = CorpusTestRun.Task(0, CorpusTestRun.Labelled());
        var primary = CorpusTestRun.Completed(task, 'a', 10, languages: [("eng", 10)], drops: [("min_words", 3)]);
        var replica = CorpusTestRun.Completed(task, 'b', 9, languages: [("deu", 9)], drops: [("min_words", 4)]);
        replica.Error = CorpusResultCodes.NOTE_TRUNCATED;

        primary.Stats!.Pdf = new CorpusPdfStatsData { In = 3, Extracted = 2, Pages = 20, Documents = 4, Fuel = 1000 };
        replica.Stats!.Pdf = new CorpusPdfStatsData { In = 3, Extracted = 2, Pages = 20, Documents = 5, Fuel = 1000 };

        Assert.That(CorpusIntegrityWriter.Differences(primary, replica), Is.EqualTo(new[] { "verdict", "content", "documents", "drops", "languages", "pdf" }));
        Assert.That(CorpusIntegrityWriter.Differences(primary, primary.Clone()), Is.Empty);
    }

    [Test]
    public void TheCodeOfAShardThatDidNotCompleteIsNotComparedTest()
    {
        // How the origin answered one machine at one moment: two honest nodes can differ in it.
        var task = CorpusTestRun.Task(0);

        Assert.That(CorpusIntegrityWriter.Differences(CorpusTestRun.Verdict(task, CorpusShardVerdict.FetchFailed, "http_503"), CorpusTestRun.Verdict(task, CorpusShardVerdict.FetchFailed, "http_500")), Is.Empty);
    }

    [Test]
    public void ThePdfCountsAreComparedAndTheFuelIsNotTest()
    {
        var task = CorpusTestRun.Task(0);
        var primary = CorpusTestRun.Completed(task);
        var replica = CorpusTestRun.Completed(task);
        primary.Stats!.Pdf = new CorpusPdfStatsData { In = 3, Extracted = 2, Pages = 20, Documents = 4, Fuel = 1000 };
        replica.Stats!.Pdf = new CorpusPdfStatsData { In = 3, Extracted = 2, Pages = 20, Documents = 4, Fuel = 2000 };

        Assert.Multiple(() =>
        {
            Assert.That(CorpusIntegrityWriter.Differences(primary, replica), Is.Empty, "the fuel is a cost, its documents are compared");
            Assert.That(CorpusIntegrityWriter.Differences(primary, CorpusTestRun.Completed(task)), Is.EqualTo(new[] { "pdf" }));
        });
    }

    [Test]
    public void TheUnsupportedBreakdownIsComparedWithTheDropsTest()
    {
        var task = CorpusTestRun.Task(0, CorpusTestRun.Labelled());
        var primary = CorpusTestRun.Completed(task, drops: [("language_unsupported", 2)]);
        primary.Stats!.UnsupportedLanguages = CorpusTestRun.Counts([("swe", 2)]);
        var replica = primary.Clone();
        replica.Stats!.UnsupportedLanguages = CorpusTestRun.Counts([("dan", 2)]);

        Assert.That(CorpusIntegrityWriter.Differences(primary, replica), Is.EqualTo(new[] { "drops" }));
    }

    #endregion

    #region Signature Tests

    [Test]
    public void SignaturesThatAgreeAreComparedAndMatchTest()
    {
        var tasks = CorpusTestRun.Tasks(1, CorpusTestRun.Signed());
        using var document = Write(tasks, CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[0]));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("signatures_compared").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("signature_mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(pair.GetProperty("match").GetBoolean(), Is.True);
            Assert.That(pair.GetProperty("signatures").GetString(), Is.EqualTo(CorpusIntegrityWriter.SIGNATURES_MATCH));
            Assert.That(pair.GetProperty("primary").GetProperty("signatures_sha256").GetString(), Is.EqualTo(CorpusTestRun.Hash('b')));
        });
    }

    [Test]
    public void SignaturesThatDifferAreAMismatchOfTheirOwnKindTest()
    {
        // The text agrees: only the signatures tell the dishonest side apart.
        var tasks = CorpusTestRun.Tasks(1, CorpusTestRun.Signed());
        using var document = Write(tasks, CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[0], signatures: 'e'));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("signature_mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.EqualTo(1.0));
            Assert.That(pair.GetProperty("match").GetBoolean(), Is.False);
            Assert.That(pair.GetProperty("signatures").GetString(), Is.EqualTo(CorpusIntegrityWriter.SIGNATURES_DIFFER));
            Assert.That(pair.GetProperty("differs").EnumerateArray().Select(field => field.GetString()), Is.EqualTo(new[] { "signatures" }));
            Assert.That(pair.GetProperty("dispute").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void APairWithoutSignaturesHasNothingOfTheKindToCompareTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        using var document = Write(tasks, CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[0]));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("signatures_compared").GetInt32(), Is.Zero, "the pipeline does not sign");
            Assert.That(pair.GetProperty("match").GetBoolean(), Is.True);
            Assert.That(pair.GetProperty("signatures").GetString(), Is.EqualTo(CorpusIntegrityWriter.SIGNATURES_NONE));
        });
    }

    #endregion

    #region Schema Tests

    [Test]
    public void TheDocumentValidatesAgainstItsSchemaTest()
    {
        // Every kind of entry the document has: a matching pair, a mismatch settled by the
        // dispute round, one that stayed open, an incomplete pair, a repaired shard, a shard
        // without a result, a dropped value.
        var tasks = CorpusTestRun.Tasks(6, CorpusTestRun.Signed());
        var stranger = CorpusTestRun.Completed(tasks[5]);
        stranger.ControllerVersion = "0.0.1";
        var first = new object?[]
        {
            CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[0]),
            CorpusTestRun.Completed(tasks[1]), CorpusTestRun.Completed(tasks[1], signatures: 'e'),
            CorpusTestRun.Completed(tasks[2]), CorpusTestRun.Completed(tasks[2], '0'),
            CorpusTestRun.Completed(tasks[3]), CorpusTestRun.Fault(tasks[3]),
            CorpusTestRun.Fault(tasks[4], CorpusResultCodes.FAULT_NETWORK),
            stranger, "garbage"
        };
        var before = CorpusTestRun.Ledger(tasks, first);
        var repairs = before.RepairTasks();
        var disputes = before.DisputeTasks();
        var ledger = CorpusTestRun.Ledger(tasks, first,
            (repairs, [CorpusTestRun.Completed(repairs[0]), CorpusTestRun.Fault(repairs[1], CorpusResultCodes.FAULT_UPLOAD)]),
            (disputes,
            [
                CorpusTestRun.Completed(disputes[0]), CorpusTestRun.Completed(disputes[0]), CorpusTestRun.Completed(disputes[0], signatures: 'e'),
                CorpusTestRun.Completed(disputes[1]), CorpusTestRun.Completed(disputes[1], '0'), CorpusTestRun.Fault(disputes[1])
            ]));

        using var document = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        using var schema = JsonDocument.Parse(File.ReadAllBytes(SchemaPath()));
        var summary = document.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(CorpusSchemaCheck.Validate(schema.RootElement, document.RootElement), Is.Empty);
            Assert.That(schema.RootElement.GetProperty("properties").GetProperty("schema").GetProperty("const").GetString(), Is.EqualTo(CorpusIntegrityWriter.SCHEMA));
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(4));
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("disputes_resolved").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("disputed").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("repairs").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("repaired").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("no_result").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.EqualTo(2));
            Assert.That(document.RootElement.GetProperty("disputes").EnumerateArray().Select(dispute => dispute.GetProperty("outcome").GetString()), Is.EqualTo(new[] { "resolved", "no_majority" }));
            Assert.That(document.RootElement.GetProperty("disputes").EnumerateArray().Select(dispute => dispute.GetProperty("first_copies").GetInt32()), Is.All.EqualTo(2));
            Assert.That(document.RootElement.GetProperty("disputes").EnumerateArray().Select(dispute => dispute.GetProperty("copies").GetInt32()), Is.EqualTo(new[] { 3, 2 }), "a node fault is no copy");
            Assert.That(document.RootElement.GetProperty("disputes").EnumerateArray().Select(dispute => dispute.GetProperty("agreeing").GetInt32()), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(document.RootElement.GetProperty("rejected").EnumerateObject().Select(reason => reason.Name), Is.EqualTo(CorpusLedger.REJECTION_REASONS), "every reason is written, 0 included");
            Assert.That(document.RootElement.GetProperty("disputes")[0].GetProperty("accepted").GetProperty("signatures_sha256").GetString(), Is.EqualTo(CorpusTestRun.Hash('b')));
            Assert.That(document.RootElement.GetProperty("no_result")[0].GetProperty("index").GetInt32(), Is.EqualTo(5));
            Assert.That(document.RootElement.GetProperty("no_result")[0].GetProperty("faults").EnumerateArray().Select(fault => fault.GetString()), Is.EqualTo(new[] { "upload" }));
        });
    }

    #endregion

    #region Tools

    private static JsonDocument Write(IReadOnlyList<CorpusTaskData> tasks, params object?[] executions)
    {
        return JsonDocument.Parse(CorpusIntegrityWriter.Write(CorpusTestRun.Ledger(tasks, executions)));
    }

    private static string SchemaPath()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "OutWit.slnx")))
            dir = Path.GetDirectoryName(dir);

        if (dir == null)
            Assert.Ignore("Solution root not found");

        return Path.Combine(dir!, "AI", "OutWit.Controller.AI.Corpus", "docs", "corpus-integrity.schema.json");
    }

    #endregion
}
