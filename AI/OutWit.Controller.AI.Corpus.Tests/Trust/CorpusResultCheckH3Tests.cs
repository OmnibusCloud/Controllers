using System.Text.Json;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Trust;

/// <summary>
/// Audit finding H-3: a result an honest node would not make — a duplicate drop name, a null where
/// the type says there is none, a count below zero, sums that do not add up — costs that result,
/// never the report or the integrity document of the run.
/// </summary>
[TestFixture]
public sealed class CorpusResultCheckH3Tests
{
    #region Constants

    /// <summary>Every way a node can bend a result out of shape; each changes one thing of an honest result.</summary>
    private static readonly (string Name, Action<CorpusResultData> Bend)[] MALFORMED =
    [
        ("duplicate drop name", result => result.Stats!.Drops = [Count("min_words", 1), Count("min_words", 2)]),
        ("drops out of order", result => result.Stats!.Drops = [Count("min_words", 1), Count("max_words", 1)]),
        ("null language name", result => result.Stats!.Languages = [Count(null!, 5)]),
        ("null count item", result => result.Stats!.Languages = [null!]),
        ("null drops list", result => result.Stats!.Drops = null!),
        ("null languages list", result => result.Stats!.Languages = null!),
        ("null unsupported list", result => result.Stats!.UnsupportedLanguages = null!),
        ("null controller version", result => result.ControllerVersion = null!),
        ("null error", result => result.Error = null!),
        ("null content hash", result => result.ContentSha256 = null!),
        ("null signatures hash", result => result.SignaturesSha256 = null!),
        ("null source URL", result => result.SourceUrl = null!),
        ("null runtime target", result => result.RuntimeTarget = null!),
        ("null node name", result => result.NodeName = null!),
        ("a node name, which no result carries", result => result.NodeName = "contributors-laptop"),
        ("a megabyte of node name", result => result.NodeName = new string('n', 1 << 20)),
        ("a node id, which no result carries", result => result.NodeId = Guid.NewGuid()),
        ("null statistics", result => result.Stats = null),
        ("a drop rule nobody has", result => result.Stats!.Drops = [Count("made_up_rule", 1)]),
        ("a language that is no code", result => result.Stats!.Languages = [Count("C:\\Users\\someone", 5)]),
        ("a negative count", result => result.Stats!.Drops = [Count("min_words", -1)]),
        ("a zero count", result => result.Stats!.Drops = [Count("min_words", 0)]),
        ("negative documents", result => result.Stats!.DocumentsOut = -1),
        ("negative records", result => result.Stats!.RecordsIn = -5),
        ("negative bytes", result => result.Stats!.BytesFetched = -1),
        ("more bytes than a shard may have", result => result.Stats!.BytesFetched = (1L << 30) + 1),
        ("negative time", result => result.Stats!.ProcessMs = -1),
        ("languages that do not sum to the documents", result => result.Stats!.DocumentsOut = 4),
        ("a funnel that does not add up", result => result.Stats!.DocumentsExtracted = 999),
        ("a content hash that is no hash", result => result.ContentSha256 = "0000"),
        ("an upper-case content hash", result => result.ContentSha256 = new string('A', 64)),
        ("free text as the error", result => result.Error = "Access to the path 'C:\\Users\\someone\\text.jsonl.zst' is denied."),
        ("a verdict that is none", result => result.Verdict = (CorpusShardVerdict)99),
        ("an output mode that is none", result => result.Output = (CorpusOutputMode)7),
        ("a negative task index", result => result.TaskIndex = -1),
        ("a degree out of range", result => result.Degree = int.MaxValue),
        ("a version that is no version", result => result.ControllerVersion = "0.4.0 <script>"),
        ("a runtime target that names a machine", result => result.RuntimeTarget = "DESKTOP/someone's laptop"),
        ("a megabyte of URL", result => result.SourceUrl = new string('u', 1 << 20)),
        ("a PDF count below zero", result => result.Stats!.Pdf = new CorpusPdfStatsData { In = 1, Pages = -1 }),
        ("more PDFs extracted than met", result => result.Stats!.Pdf = new CorpusPdfStatsData { In = 1, Extracted = 2 }),
        ("more PDF fuel than a shard may burn", result => result.Stats!.Pdf = new CorpusPdfStatsData { In = 1, Fuel = OutWit.Controller.AI.Corpus.Pdf.PdfStageLimits.SHARD_FUEL + OutWit.Controller.AI.Corpus.Pdf.PdfStageLimits.FUEL + 1 }),
        ("signatures without parameters", result => result.SignaturesSha256 = CorpusTestRun.Hash('b')),
        ("MinHash parameters nobody can use", result => { result.MinHash = new CorpusMinHashData { Hashes = 100, ShingleWords = 5, Bands = 16 }; result.SignaturesSha256 = CorpusTestRun.Hash('b'); }),
        ("a ticket that is no ticket", result => result.Ticket = "DEADBEEF")
    ];

    #endregion

    #region Tests

    [Test]
    public void H3AnHonestResultIsWellFormedTest()
    {
        var task = CorpusTestRun.Task(0, CorpusTestRun.Labelled());
        var signed = CorpusTestRun.Task(1, CorpusTestRun.Signed());

        Assert.Multiple(() =>
        {
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Completed(task, drops: [("min_words", 2), ("http_status", 3), ("language_unsupported", 1)])), Is.False, "the unsupported breakdown must sum to its drop");
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Completed(task, drops: [("min_words", 2), ("http_status", 3)])), Is.True);
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Completed(signed)), Is.True);
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Verdict(task, CorpusShardVerdict.FetchFailed, "http_503")), Is.True);
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Verdict(task, CorpusShardVerdict.Oversized, "oversized")), Is.True);
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Fault(task, CorpusResultCodes.FAULT_PDF_BACKSTOP)), Is.True);
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Verdict(task, CorpusShardVerdict.FetchFailed, "disk")), Is.False, "a fault's code under a verdict");
            Assert.That(CorpusResultCheck.IsWellFormed(CorpusTestRun.Fault(task, "http_503")), Is.False, "a verdict's code under a fault");
            Assert.That(CorpusResultCheck.IsWellFormed(null), Is.False);
        });
    }

    [Test]
    public void H3EveryMalformedResultIsRefusedByTheCheckTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (name, bend) in MALFORMED)
            {
                var result = CorpusTestRun.Completed(CorpusTestRun.Task(0, CorpusTestRun.Labelled()));
                bend(result);

                Assert.That(CorpusResultCheck.IsWellFormed(result), Is.False, name);
            }
        });
    }

    [Test]
    public void H3AMalformedResultCostsThatResultNotTheRunTest()
    {
        // The audit's reproduction threw out of CorpusReportWriter.Write and CorpusIntegrityWriter.Write
        // (ArgumentException, ArgumentNullException, NullReferenceException) for the whole run.
        Assert.Multiple(() =>
        {
            foreach (var (name, bend) in MALFORMED)
            {
                var tasks = CorpusTestRun.Tasks(3, CorpusTestRun.Labelled());
                var bent = CorpusTestRun.Completed(tasks[1]);
                bend(bent);

                // As a node sends it: the wire carries the nulls.
                var wire = MemoryPackSerializer.Deserialize<CorpusResultData>(MemoryPackSerializer.Serialize(bent));

                CorpusLedger? ledger = null;
                byte[] report = [], integrity = [];
                Assert.DoesNotThrow(() =>
                {
                    ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[0]), wire, CorpusTestRun.Completed(tasks[2])]);
                    report = CorpusReportWriter.Write(ledger);
                    integrity = CorpusIntegrityWriter.Write(ledger);
                }, name);

                using var document = JsonDocument.Parse(report);
                var shards = document.RootElement.GetProperty("shards").EnumerateArray().ToList();

                Assert.That(shards.Select(shard => shard.GetProperty("verdict").GetString()), Is.EqualTo(new[] { "Completed", "NoResult", "Completed" }), name);
                Assert.That(ledger!.RejectedResults, Is.EqualTo(1), name);
                Assert.That(ledger.RepairTasks().Select(task => task.TaskIndex), Is.EqualTo(new[] { 1 }), $"{name}: the shard is sent out once more");

                using var integrityDocument = JsonDocument.Parse(integrity);
                Assert.That(integrityDocument.RootElement.GetProperty("summary").GetProperty("rejected_results").GetInt32(), Is.EqualTo(1), name);
                Assert.That(integrityDocument.RootElement.GetProperty("summary").GetProperty("compared").GetInt32(), Is.EqualTo(1), $"{name}: the honest pair is still compared");
            }
        });
    }

    [Test]
    public void H3AResultAtOddsWithItsTaskIsRejectedTest()
    {
        (string Name, CorpusPipelineData Pipeline, Action<CorpusResultData> Bend)[] cases =
        [
            ("another output mode", CorpusTestRun.Plain(), result => result.Output = CorpusOutputMode.StatsAndHashes),
            ("a text blob the run does not keep", new CorpusPipelineData { IdentifyLanguage = false, Sign = false, Output = CorpusOutputMode.StatsAndHashes }, result => result.TextBlobId = Guid.NewGuid()),
            ("no text blob where the run keeps one", CorpusTestRun.Plain(), result => result.TextBlobId = Guid.Empty),
            ("other MinHash parameters", CorpusTestRun.Signed(), result => result.MinHash = new CorpusMinHashData { Hashes = 256, ShingleWords = 5, Bands = 16 }),
            ("no signatures where the pipeline signs", CorpusTestRun.Signed(), result => { result.MinHash = null; result.SignaturesSha256 = ""; result.SignaturesBlobId = Guid.Empty; }),
            ("languages where the pipeline labels none", CorpusTestRun.Plain(), result => result.Stats!.Languages = [Count("eng", 5)]),
            ("a language the run does not keep", new CorpusPipelineData { IdentifyLanguage = true, TargetLanguages = ["deu"], Sign = false }, result => result.Stats!.Languages = [Count("eng", 5)])
        ];

        Assert.Multiple(() =>
        {
            foreach (var (name, pipeline, bend) in cases)
            {
                var tasks = CorpusTestRun.Tasks(1, pipeline);
                var result = CorpusTestRun.Completed(tasks[0], languages: pipeline.IdentifyLanguage ? [("deu", 5)] : null);
                bend(result);

                var ledger = CorpusTestRun.Ledger(tasks, [result]);

                Assert.That(ledger.Rejected.Keys, Is.EqualTo(new[] { CorpusLedger.REJECTED_INCONSISTENT }), name);
            }
        });
    }

    #endregion

    #region Tools

    private static CorpusCountData Count(string name, int count)
    {
        return new CorpusCountData { Name = name, Count = count };
    }

    #endregion
}
