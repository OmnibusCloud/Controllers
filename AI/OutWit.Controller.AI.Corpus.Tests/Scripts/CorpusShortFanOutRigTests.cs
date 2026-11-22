using System.Collections;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Scripts;

/// <summary>
/// A fan-out that comes back short, through the engine: the bundled CorpusProcess.wit over three
/// mock nodes below the real Grid controller. One node answers a task with a value that is no
/// answer. What the platform then hands the script depends on the Grid in the rig — 1.3.0 leaves
/// the whole round's answers out of the collection, a later one leaves that one value out — so
/// every assertion here holds for both, and what holds for one of them only says which. Either
/// way the run keeps its checking: no forged result of that node is a product, and both documents
/// say that the sampling the run asked for did not come back as asked.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusShortFanOutRigTests : CorpusScriptTestBase
{
    #region Constants

    private const string WET_FIXTURE = CorpusFixtures.WET;
    private const string MODEL_FIXTURE = CorpusFixtures.PACK;
    private const int SHARDS = 4;

    private static readonly string FORGED_HASH = new('0', 64);

    /// <summary>
    /// The script of Corpus.Scripts 0.1.5, for controllers up to 0.6.0: the repair round on one
    /// machine, and no options handed to Corpus.Integrity.
    /// </summary>
    private const string SCRIPT_OF_AN_EARLIER_CONTROLLER = """
        Job:CorpusProcess(Blob:manifest, Blob:languageModel, CorpusOptions:opts)
        {
            CorpusTaskPackageCollection:tasks = Corpus.Split(manifest, languageModel, opts);

            Double:rate = Corpus.SamplingRate(opts);
            ProcessingOptions:checked = Grid.Replicas(2, rate);

            CorpusResultPackageCollection:executions = Grid.ForEach(package in tasks, checked)
                => Corpus.ProcessPackage(package);

            CorpusTaskPackageCollection:repairs = Corpus.Repairs(tasks, executions);

            CorpusResultPackageCollection:repaired = Grid.ForEach(package in repairs)
                => Corpus.ProcessPackage(package);

            CorpusTaskPackageCollection:disputes = Corpus.Disputes(tasks, executions, repairs, repaired);
            ProcessingOptions:majority = Grid.Replicas(3);

            CorpusResultPackageCollection:resolved = Grid.ForEach(package in disputes, majority)
                => Corpus.ProcessPackage(package);

            Blob:report = Corpus.Collect(tasks, executions, repairs, repaired, disputes, resolved, opts);
            Blob:integrity = Corpus.Integrity(tasks, executions, repairs, repaired, disputes, resolved);
        }
        """;

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

        // Four listings of one generated shard: four tasks with one honest product.
        m_manifestBlob = await UploadAsync(Manifest(Enumerable.Repeat(WET_FIXTURE, SHARDS).ToArray()), "manifest.json");

        using var honest = await RunAsync(m_script, 0);
        m_honestContent = honest.Contents.First()!;
    }

    [TearDown]
    public void Honest()
    {
        NodesManager.Tamper = null;
        NodesManager.Null = null;
    }

    #endregion

    #region Tests

    [Test]
    public async Task AnHonestRunSaysItsSamplingCameBackAsAskedTest()
    {
        using var sampled = await RunAsync(m_script, 1.0);
        using var plain = await RunAsync(m_script, 0);

        Assert.Multiple(() =>
        {
            Assert.That(sampled.Sampling("state"), Is.EqualTo("as_asked"));
            Assert.That(sampled.Integrity.RootElement.GetProperty("sampling").GetProperty("rate").GetDouble(), Is.EqualTo(1.0));
            Assert.That(sampled.Counts("sampling", "packages", "asked_twice", "answered_twice", "compared"), Is.EqualTo(new[] { SHARDS, SHARDS, SHARDS, SHARDS }));
            Assert.That(sampled.Counts("rounds.first", "packages", "answers", "unanswered", "answered_twice"), Is.EqualTo(new[] { SHARDS, 2 * SHARDS, 0, SHARDS }));
            Assert.That(sampled.Counts("rounds.repair", "packages", "answers"), Is.EqualTo(new[] { 0, 0 }), "no round is run for nothing");
            Assert.That(sampled.Counts("rounds.dispute", "packages", "answers"), Is.EqualTo(new[] { 0, 0 }));
            Assert.That(sampled.Report.RootElement.GetProperty("sampling").GetRawText(), Is.EqualTo(sampled.Integrity.RootElement.GetProperty("sampling").GetRawText()), "the report says the same");
            Assert.That(sampled.Report.RootElement.GetProperty("rounds").GetRawText(), Is.EqualTo(sampled.Integrity.RootElement.GetProperty("rounds").GetRawText()));
            Assert.That(sampled.Variables("rechecked"), Is.Not.Null, "the repair round has its own options");

            Assert.That(plain.Sampling("state"), Is.EqualTo("not_asked"));
            Assert.That(plain.Counts("rounds.first", "packages", "answers", "answered_once"), Is.EqualTo(new[] { SHARDS, SHARDS, SHARDS }));
            Assert.That(plain.Contents, Is.All.EqualTo(m_honestContent));
            Assert.That(sampled.Contents, Is.All.EqualTo(m_honestContent));
        });
    }

    [Test]
    public async Task ANullAnswerDoesNotStripTheRunOfItsCheckingTest()
    {
        // The audit's reproduction, in Verify: every task was to run on two machines; one node
        // answered one slot with null, the first round's answers never reached the script, every
        // task was repaired on one machine, and what the same node said in that round stood. Here
        // the node answers its first slot with null and forges the product of every shard it
        // answers after that.
        var liar = NodesManager.CompatibleNodes[0].NodeId;
        var silent = 0;
        NodesManager.Null = (nodeId, _) => nodeId == liar && Interlocked.Exchange(ref silent, 1) == 0;
        NodesManager.Tamper = (nodeId, result) =>
        {
            if (nodeId == liar && result.Verdict == CorpusShardVerdict.Completed)
                result.ContentSha256 = FORGED_HASH;
        };

        using var run = await RunAsync(m_script, 1.0);
        var summary = run.Integrity.RootElement.GetProperty("summary");
        var firstAnswers = run.Counts("rounds.first", "answers")[0];

        TestContext.Out.WriteLine($"the first round brought the script {firstAnswers} of {2 * SHARDS} answers; sampling {run.Sampling("state")}: {run.Sampling("statement")}");

        Assert.Multiple(() =>
        {
            Assert.That(silent, Is.EqualTo(1), "the node did answer a slot with nothing");
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(run.Status));
            Assert.That(run.Verdicts, Is.All.EqualTo("Completed"));
            Assert.That(run.Contents, Is.All.EqualTo(m_honestContent), "no forged product is in the report");

            // Both documents say that the checking the run asked for did not come back as asked.
            Assert.That(run.Sampling("state"), Is.AnyOf("none", "short"));
            Assert.That(run.Report.RootElement.GetProperty("sampling").GetProperty("state").GetString(), Is.EqualTo(run.Sampling("state")));
            Assert.That(run.Counts("sampling", "asked_twice")[0], Is.EqualTo(SHARDS));
            Assert.That(run.Counts("sampling", "answered_twice")[0], Is.LessThan(SHARDS));
            Assert.That(firstAnswers, Is.LessThan(2 * SHARDS), "the round came back short");

            // Whatever the node forged met a second machine: its lies are disputes, and settled.
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.GreaterThan(0));
            Assert.That(summary.GetProperty("disputes_resolved").GetInt32(), Is.EqualTo(summary.GetProperty("disputes").GetInt32()));

            if (firstAnswers == 0)
            {
                // Grid 1.3.0: one value that is no answer and the round's collection stays empty.
                // Every shard goes out again, and on two machines.
                Assert.That(run.Sampling("state"), Is.EqualTo("none"));
                Assert.That(run.Counts("rounds.first", "unanswered")[0], Is.EqualTo(SHARDS));
                Assert.That(run.Counts("rounds.repair", "packages", "answers", "answered_twice"), Is.EqualTo(new[] { SHARDS, 2 * SHARDS, SHARDS }), "the repair round ran every shard twice");
                Assert.That(summary.GetProperty("repair_pairs").GetInt32(), Is.EqualTo(SHARDS));
                Assert.That(summary.GetProperty("repair_compared").GetInt32(), Is.EqualTo(SHARDS));
                Assert.That(summary.GetProperty("single_execution").GetInt32(), Is.Zero, "no shard rests on one machine");
                Assert.That(summary.GetProperty("repair_mismatches").GetInt32(), Is.EqualTo(summary.GetProperty("disputes").GetInt32()), "each dispute came from a pair of the repair round");
            }
            else
            {
                // A later Grid leaves the one value out: its package came back from one machine.
                Assert.That(run.Sampling("state"), Is.EqualTo("short"));
                Assert.That(firstAnswers, Is.EqualTo(2 * SHARDS - 1));
            }
        });
    }

    [Test]
    public async Task ANullAnswerInTheDisputeRoundIsSaidByTheDocumentsTest()
    {
        // The node forges every product in the first round (a dispute for each shard it held a
        // copy of) and answers one slot of the dispute round with null. A shard may then stay
        // disputed; no forged product stands, and the documents say the round came back short.
        var liar = NodesManager.CompatibleNodes[0].NodeId;
        var answers = 0;
        var silent = 0;
        NodesManager.Null = (nodeId, _) => Interlocked.Increment(ref answers) > 2 * SHARDS && nodeId == liar && Interlocked.Exchange(ref silent, 1) == 0;
        NodesManager.Tamper = (nodeId, result) =>
        {
            if (nodeId == liar && result.Verdict == CorpusShardVerdict.Completed)
                result.ContentSha256 = FORGED_HASH;
        };

        using var run = await RunAsync(m_script, 1.0);
        var summary = run.Integrity.RootElement.GetProperty("summary");
        var dispute = run.Counts("rounds.dispute", "packages", "answers", "answered_thrice");

        TestContext.Out.WriteLine($"the dispute round: {dispute[0]} package(s), {dispute[1]} answer(s), {dispute[2]} answered by three; {summary.GetProperty("disputes_resolved").GetInt32()} of {summary.GetProperty("disputes").GetInt32()} dispute(s) settled");

        Assert.Multiple(() =>
        {
            Assert.That(silent, Is.EqualTo(1), "the node did answer a slot of the dispute round with nothing");
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(run.Status));
            Assert.That(run.Sampling("state"), Is.EqualTo("as_asked"), "the first round came back whole");
            Assert.That(dispute[0], Is.GreaterThan(0), "the forged products were disputed");
            Assert.That(dispute[1], Is.LessThan(3 * dispute[0]), "the documents say the dispute round came back short");
            Assert.That(dispute[2], Is.LessThan(dispute[0]));
            Assert.That(run.Verdicts, Is.All.AnyOf("Completed", "Disputed"));
            Assert.That(run.Contents, Is.All.AnyOf(m_honestContent, ""), "a shard is the honest product or has none: no forged product stands");
            Assert.That(summary.GetProperty("disputed").GetInt32(), Is.EqualTo(run.Verdicts.Count(verdict => verdict == "Disputed")));
        });
    }

    [Test]
    public async Task AScriptWrittenForAnEarlierControllerStillRunsTest()
    {
        // The scripts package before this one claims every controller up to 2.0.0: its script
        // runs here as it ran, the repair round on one machine, and its integrity document says
        // what it was not told.
        using var run = await RunAsync(SCRIPT_OF_AN_EARLIER_CONTROLLER, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(run.Status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(run.Status));
            Assert.That(run.Verdicts, Is.All.EqualTo("Completed"));
            Assert.That(run.Contents, Is.All.EqualTo(m_honestContent));
            Assert.That(run.Executions, Is.All.EqualTo(2));
            Assert.That(run.Integrity.RootElement.GetProperty("sampling").GetProperty("rate").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(run.Sampling("state"), Is.EqualTo("unknown"));
            Assert.That(run.Report.RootElement.GetProperty("sampling").GetProperty("state").GetString(), Is.EqualTo("as_asked"), "Corpus.Collect was handed the options by that script too");
            Assert.That(run.Integrity.RootElement.GetProperty("summary").GetProperty("compared").GetInt32(), Is.EqualTo(SHARDS));
        });
    }

    #endregion

    #region Tools

    private async Task<Run> RunAsync(string script, double samplingRate)
    {
        var job = Engine.Compile(script);
        var status = await Engine.ScheduleAndWaitAsync(job, m_manifestBlob, m_modelBlob, new CorpusOptionsData { SamplingRate = samplingRate });
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(status));

        return new Run(name => job.Variables[name].Value, status, await ReadBytesAsync((Guid)job.Variables["report"].Value!), await ReadBytesAsync((Guid)job.Variables["integrity"].Value!));
    }

    #endregion

    #region Types

    /// <summary>A finished run: its status, its variables and its two documents.</summary>
    private sealed class Run(Func<string, object?> variable, IWitProcessingStatus status, byte[] report, byte[] integrity) : IDisposable
    {
        public IWitProcessingStatus Status { get; } = status;

        public JsonDocument Report { get; } = JsonDocument.Parse(report);

        public JsonDocument Integrity { get; } = JsonDocument.Parse(integrity);

        public IEnumerable<string?> Verdicts => Report.RootElement.GetProperty("shards").EnumerateArray().Select(shard => shard.GetProperty("verdict").GetString());

        public IEnumerable<string?> Contents => Report.RootElement.GetProperty("shards").EnumerateArray().Select(shard => shard.GetProperty("content_sha256").GetString());

        public IEnumerable<int> Executions => Report.RootElement.GetProperty("shards").EnumerateArray().Select(shard => shard.GetProperty("executions").GetInt32());

        /// <summary>A word of the integrity document's account of the sampling.</summary>
        public string? Sampling(string name)
        {
            return Integrity.RootElement.GetProperty("sampling").GetProperty(name).GetString();
        }

        /// <summary>Numbers of the integrity document: the object at a dotted path, then the properties named.</summary>
        public int[] Counts(string path, params string[] names)
        {
            var element = Integrity.RootElement;
            foreach (var part in path.Split('.'))
                element = element.GetProperty(part);

            return names.Select(name => element.GetProperty(name).GetInt32()).ToArray();
        }

        public object? Variables(string name)
        {
            return variable(name);
        }

        public void Dispose()
        {
            Report.Dispose();
            Integrity.Dispose();
        }
    }

    #endregion
}
