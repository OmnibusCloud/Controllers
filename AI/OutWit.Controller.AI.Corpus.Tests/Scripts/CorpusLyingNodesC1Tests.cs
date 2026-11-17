using System.Collections;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Scripts;

/// <summary>
/// Audit finding C-1 through the engine: the bundled CorpusProcess.wit over three mock nodes of
/// which one lies. Whatever it does to the results it returns — another version, another shard's
/// index, an empty or a forged content hash, other signatures, a shape no honest node gives, no
/// answer at all — the run completes, the honest product stands or the shard is named as having
/// none, and the report and the integrity document say what happened. The nodes sit below the real
/// Grid controller, so a node that returns more values than its tasks asked for meets Grid's own
/// rule first.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusLyingNodesC1Tests : CorpusScriptTestBase
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string MODEL_FIXTURE = "language-pack-cc-2026-43-16.bin";
    private const int SHARDS = 4;

    private static readonly string FORGED_HASH = new('0', 64);

    #endregion

    #region Fields

    private string m_script = null!;
    private Guid m_modelBlob;
    private Guid m_manifestBlob;
    private string m_honestContent = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_script = LoadScript("CorpusProcess.wit");
        m_modelBlob = await UploadFixtureAsync(MODEL_FIXTURE);

        // Four listings of one golden shard: four tasks with one honest product.
        m_manifestBlob = await UploadAsync(Manifest(Enumerable.Repeat(WET_FIXTURE, SHARDS).ToArray()), "manifest.json");

        var honest = await RunAsync(0, null);
        m_honestContent = honest.Report.RootElement.GetProperty("shards")[0].GetProperty("content_sha256").GetString()!;
        honest.Dispose();
    }

    [TearDown]
    public void Honest()
    {
        NodesManager.Tamper = null;
        NodesManager.Twice = null;
    }

    #endregion

    #region Tests

    [Test]
    public async Task C1AnHonestRunHasNothingToSayTest()
    {
        using var run = await RunAsync(1.0, null);
        var summary = run.Integrity.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(run.Verdicts, Is.All.EqualTo("Completed"));
            Assert.That(run.Contents, Is.All.EqualTo(m_honestContent));
            Assert.That(run.Executions, Is.All.EqualTo(2), "every shard was sampled and its copies agreed");
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(SHARDS));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("repairs").GetInt32(), Is.Zero);
            Assert.That(run.Variables("repairs"), Is.Empty, "no round is run for nothing");
            Assert.That(run.Variables("disputes"), Is.Empty);
            Assert.That(run.Variables("resolved"), Is.Empty);
        });
    }

    [Test]
    public async Task C1AForgedVersionIsDroppedAndTheHonestCopyStandsTest()
    {
        // The audit's first scenario: a false product under another version switched the
        // comparison off (version_skew 1, compared 0).
        using var run = await RunAsync(1.0, Liar(result =>
        {
            result.ControllerVersion = "0.3.1";
            result.ContentSha256 = FORGED_HASH;
        }));
        var summary = run.Integrity.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), run.Status.ToString());
            Assert.That(run.Verdicts, Is.All.EqualTo("Completed"));
            Assert.That(run.Contents, Is.All.EqualTo(m_honestContent), "no forged product is in the report");
            Assert.That(summary.GetProperty("version_skew").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.GreaterThan(0));
            Assert.That(run.Integrity.RootElement.GetProperty("rejected").GetProperty("version").GetInt32(), Is.EqualTo(summary.GetProperty("rejected_results").GetInt32()));
            Assert.That(run.Report.RootElement.GetProperty("summary").GetProperty("rejected_results").GetInt32(), Is.EqualTo(summary.GetProperty("rejected_results").GetInt32()));
        });
    }

    [Test]
    public async Task C1AResultUnderAForeignTaskIndexAnswersNothingTest()
    {
        // The audit's third scenario: a node returns its result under the index of a shard it
        // does not hold, with a URL of its own. Without sampling the liar's shards rest on it alone.
        using var run = await RunAsync(0, Liar(result =>
        {
            result.TaskIndex = (result.TaskIndex + 1) % SHARDS;
            result.SourceUrl = "https://attacker.example/anything";
            result.ContentSha256 = FORGED_HASH;
        }, onlyFirstAnswerPerShard: true));
        var summary = run.Report.RootElement.GetProperty("summary");
        var text = Encoding.UTF8.GetString(run.ReportBytes) + Encoding.UTF8.GetString(run.IntegrityBytes);

        Assert.Multiple(() =>
        {
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), run.Status.ToString());
            Assert.That(summary.GetProperty("tasks").GetInt32(), Is.EqualTo(SHARDS), "no shard is missing from the report");
            Assert.That(run.Verdicts, Is.All.EqualTo("Completed"), "the shards whose answers were dropped were sent out once more");
            Assert.That(run.Contents, Is.All.EqualTo(m_honestContent));
            Assert.That(text, Does.Not.Contain("attacker.example"));
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.GreaterThan(0));
            Assert.That(summary.GetProperty("repairs").GetInt32(), Is.EqualTo(summary.GetProperty("rejected_results").GetInt32()));
            Assert.That(run.Integrity.RootElement.GetProperty("rejected").GetProperty("content").GetInt32(), Is.EqualTo(summary.GetProperty("rejected_results").GetInt32()));
        });
    }

    [Test]
    public async Task C1AForgedContentHashIsADisputeTheHonestMachinesWinTest()
    {
        // The audit's second scenario: of two completed executions the lower hash stood for the
        // shard. Now the copies disagree, three machines run the shard, and two of them are honest.
        using var run = await RunAsync(1.0, Liar(result => result.ContentSha256 = FORGED_HASH));
        var summary = run.Integrity.RootElement.GetProperty("summary");
        var disputes = run.Integrity.RootElement.GetProperty("disputes").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), run.Status.ToString());
            Assert.That(run.Verdicts, Is.All.EqualTo("Completed"));
            Assert.That(run.Contents, Is.All.EqualTo(m_honestContent), "the lower hash does not win");
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.GreaterThan(0));
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.EqualTo(summary.GetProperty("mismatches").GetInt32()));
            Assert.That(summary.GetProperty("disputes_resolved").GetInt32(), Is.EqualTo(summary.GetProperty("disputes").GetInt32()));
            Assert.That(summary.GetProperty("disputed").GetInt32(), Is.Zero);
            Assert.That(disputes.Select(dispute => dispute.GetProperty("outcome").GetString()), Is.All.EqualTo("resolved"));
            Assert.That(disputes.Select(dispute => dispute.GetProperty("results").GetArrayLength()), Is.All.EqualTo(3), "three machines");
            Assert.That(disputes.Select(dispute => dispute.GetProperty("accepted").GetProperty("content_sha256").GetString()), Is.All.EqualTo(m_honestContent));
            Assert.That(run.Report.RootElement.GetProperty("summary").GetProperty("disputes_resolved").GetInt32(), Is.EqualTo(summary.GetProperty("disputes").GetInt32()));
        });
    }

    [Test]
    public async Task C1AnEmptyContentHashIsNoResultOfThatNodeTest()
    {
        using var run = await RunAsync(1.0, Liar(result => result.ContentSha256 = ""));

        Assert.Multiple(() =>
        {
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), run.Status.ToString());
            Assert.That(run.Contents, Is.All.EqualTo(m_honestContent), "the empty hash sorted first and stood for the shard; now it is not a result");
            Assert.That(run.Integrity.RootElement.GetProperty("rejected").GetProperty("unreadable").GetInt32(), Is.GreaterThan(0));
            Assert.That(run.Integrity.RootElement.GetProperty("summary").GetProperty("disputes").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task C1AlteredSignaturesAreADisputeTheHonestMachinesWinTest()
    {
        // The right text with other signatures: what would poison the near-duplicate merge.
        using var run = await RunAsync(1.0, Liar(result => result.SignaturesSha256 = FORGED_HASH));
        var summary = run.Integrity.RootElement.GetProperty("summary");
        var honestSignatures = run.Report.RootElement.GetProperty("shards").EnumerateArray().Select(shard => shard.GetProperty("signatures_sha256").GetString()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), run.Status.ToString());
            Assert.That(honestSignatures, Is.All.Not.EqualTo(FORGED_HASH));
            Assert.That(honestSignatures.Distinct().Count(), Is.EqualTo(1), "one shard listed four times: one honest signatures hash");
            Assert.That(summary.GetProperty("signature_mismatches").GetInt32(), Is.GreaterThan(0));
            Assert.That(summary.GetProperty("disputes_resolved").GetInt32(), Is.EqualTo(summary.GetProperty("signature_mismatches").GetInt32()));
        });
    }

    [Test]
    public async Task H3AMalformedResultCostsThatResultNotTheRunTest()
    {
        // A duplicate drop name, a null list, a null name: each threw out of Corpus.Collect and
        // the run had no report although every product was on the server.
        var kind = 0;
        using var run = await RunAsync(0, Liar(result =>
        {
            switch (kind++ % 3)
            {
                case 0:
                    result.Stats!.Drops = [new CorpusCountData { Name = "min_words", Count = 1 }, new CorpusCountData { Name = "min_words", Count = 2 }];
                    break;
                case 1:
                    result.Stats!.Languages = null!;
                    break;
                default:
                    result.ControllerVersion = null!;
                    break;
            }
        }, onlyFirstAnswerPerShard: true));
        var summary = run.Report.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), run.Status.ToString());
            Assert.That(summary.GetProperty("tasks").GetInt32(), Is.EqualTo(SHARDS));
            Assert.That(run.Verdicts, Is.All.EqualTo("Completed"), "the shards whose results were dropped were sent out once more");
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.GreaterThan(0));
            Assert.That(run.Integrity.RootElement.GetProperty("rejected").GetProperty("unreadable").GetInt32(), Is.EqualTo(summary.GetProperty("rejected_results").GetInt32()));
        });
    }

    [Test]
    public async Task C1AShardNobodyAnswersIsReportedTest()
    {
        // Whichever machine gets shard 2 answers it under no ticket, in the first round and in the
        // repair round: the shard has no result, and the report says so instead of being short.
        using var run = await RunAsync(0, (_, result) =>
        {
            if (result.TaskIndex == 2)
                result.Ticket = "";
        });
        var shards = run.Report.RootElement.GetProperty("shards").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), run.Status.ToString());
            Assert.That(shards, Has.Count.EqualTo(SHARDS));
            Assert.That(run.Verdicts, Is.EqualTo(new[] { "Completed", "Completed", "NoResult", "Completed" }));
            Assert.That(shards[2].GetProperty("reason").GetString(), Is.EqualTo("no_answer"));
            Assert.That(shards[2].GetProperty("text_blob").GetString(), Is.Empty);
            Assert.That(shards[2].GetProperty("url").GetString(), Is.EqualTo(Origin.ShardUrl(WET_FIXTURE)), "the URL is the task's");
            Assert.That(run.Integrity.RootElement.GetProperty("summary").GetProperty("no_result").GetInt32(), Is.EqualTo(1));
            Assert.That(run.Integrity.RootElement.GetProperty("no_result")[0].GetProperty("index").GetInt32(), Is.EqualTo(2));
            Assert.That(run.Integrity.RootElement.GetProperty("rejected").GetProperty("unknown_ticket").GetInt32(), Is.EqualTo(2), "the first answer and the second");
            Assert.That(run.Report.RootElement.GetProperty("summary").GetProperty("repairs").GetInt32(), Is.EqualTo(1), "it was sent out once more first");
        });
    }

    [Test]
    public async Task ASecondValueForOneTaskIsLeftOutByGridAndCausesNoDisputeTest()
    {
        // One node returns the first shard it completes twice and answers every other task it
        // was given: its call hands Grid one value more than its tasks asked for. Grid 1.3.0
        // keeps one value for each return variable that was asked for and leaves the second out,
        // so the server sees one value per task. Every shard is sampled here: a third value under
        // a ticket would have sent its shard to a dispute (the ledger's own tests hold that).
        var liar = NodesManager.CompatibleNodes[0].NodeId;
        var answered = 0;
        var values = NodesManager.ValuesReturned;
        var surplus = NodesManager.SurplusReturned;
        NodesManager.Twice = (nodeId, answer) => nodeId == liar && answer.Fault.Length == 0 && Interlocked.Exchange(ref answered, 1) == 0;

        using var run = await RunAsync(1.0, null);
        var summary = run.Integrity.RootElement.GetProperty("summary");
        var answers = run.Variables("executions").OfType<CorpusResultPackageData>().ToList();
        var executions = ResultsOf(answers);

        Assert.Multiple(() =>
        {
            Assert.That(NodesManager.SurplusReturned - surplus, Is.EqualTo(1), "the node did return a second value");
            Assert.That(NodesManager.ValuesReturned - values, Is.EqualTo(2 * SHARDS + 1), "one more than the tasks of the run");
            Assert.That(answers, Has.Count.EqualTo(2 * SHARDS), "one answer per task reached the server: Grid left the second out");
            Assert.That(answers.GroupBy(answer => answer.Ticket).Select(copies => copies.Count()), Is.All.EqualTo(2), "a package and its copy under every package ticket, no third");
            Assert.That(executions, Has.Count.EqualTo(2 * SHARDS));
            Assert.That(executions.GroupBy(result => result.Ticket).Select(copies => copies.Count()), Is.All.EqualTo(2), "a shard and its copy under every ticket, no third");
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(run.Status));
            Assert.That(run.Verdicts, Is.All.EqualTo("Completed"));
            Assert.That(run.Contents, Is.All.EqualTo(m_honestContent));
            Assert.That(run.Executions, Is.All.EqualTo(2));
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.Zero, "the second value caused no dispute");
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.Zero, "and never reached the ledger");
            Assert.That(summary.GetProperty("repairs").GetInt32(), Is.Zero);
            Assert.That(run.Variables("disputes"), Is.Empty);
        });
    }

    [Test]
    public async Task C1NoTicketLeavesTheServerInADocumentTest()
    {
        using var run = await RunAsync(1.0, Liar(result => result.ContentSha256 = FORGED_HASH));
        var documents = Encoding.UTF8.GetString(run.ReportBytes) + Encoding.UTF8.GetString(run.IntegrityBytes);
        var packages = new[] { "tasks", "repairs", "disputes" }.SelectMany(run.Variables).OfType<CorpusTaskPackageData>().ToList();
        var tickets = packages.Select(package => package.Ticket).Concat(TasksOf(packages).Select(task => task.Ticket)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(tickets, Has.Count.GreaterThan(2 * SHARDS), "the first round's and the dispute round's, of the packages and of the shards in them");
            Assert.That(tickets, Is.All.Matches<string>(CorpusTickets.IsWellFormed));
            Assert.That(tickets, Is.Unique, "a ticket per package and per shard, per round");
            foreach (var ticket in tickets)
                Assert.That(documents, Does.Not.Contain(ticket));
        });
    }

    #endregion

    #region Tools

    /// <summary>One node of the three lies: it bends every result it returns (or only its first answer for each shard).</summary>
    private Action<Guid, CorpusResultData> Liar(Action<CorpusResultData> bend, bool onlyFirstAnswerPerShard = false)
    {
        var liar = NodesManager.CompatibleNodes[0].NodeId;
        var answered = new HashSet<int>();

        return (nodeId, result) =>
        {
            if (nodeId != liar || result.Verdict != CorpusShardVerdict.Completed)
                return;

            lock (answered)
            {
                if (onlyFirstAnswerPerShard && !answered.Add(result.TaskIndex))
                    return;

                bend(result);
            }
        };
    }

    private async Task<Run> RunAsync(double samplingRate, Action<Guid, CorpusResultData>? tamper)
    {
        NodesManager.Tamper = tamper;
        try
        {
            var job = Engine.Compile(m_script);
            var status = await Engine.ScheduleAndWaitAsync(job, m_manifestBlob, m_modelBlob, new CorpusOptionsData { SamplingRate = samplingRate });
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

            return new Run(name => job.Variables[name].Value, status, await ReadBytesAsync((Guid)job.Variables["report"].Value!), await ReadBytesAsync((Guid)job.Variables["integrity"].Value!));
        }
        finally
        {
            NodesManager.Tamper = null;
        }
    }

    #endregion

    #region Types

    /// <summary>A finished run: its status, its variables and its two documents.</summary>
    private sealed class Run(Func<string, object?> variable, IWitProcessingStatus status, byte[] report, byte[] integrity) : IDisposable
    {
        public IWitProcessingStatus Status { get; } = status;

        public byte[] ReportBytes { get; } = report;

        public byte[] IntegrityBytes { get; } = integrity;

        public JsonDocument Report { get; } = JsonDocument.Parse(report);

        public JsonDocument Integrity { get; } = JsonDocument.Parse(integrity);

        public IEnumerable<string?> Verdicts => Report.RootElement.GetProperty("shards").EnumerateArray().Select(shard => shard.GetProperty("verdict").GetString());

        public IEnumerable<string?> Contents => Report.RootElement.GetProperty("shards").EnumerateArray().Select(shard => shard.GetProperty("content_sha256").GetString());

        public IEnumerable<int> Executions => Report.RootElement.GetProperty("shards").EnumerateArray().Select(shard => shard.GetProperty("executions").GetInt32());

        public IEnumerable<object?> Variables(string name)
        {
            return variable(name) is IEnumerable items ? items.Cast<object?>() : [];
        }

        public void Dispose()
        {
            Report.Dispose();
            Integrity.Dispose();
        }
    }

    #endregion
}
