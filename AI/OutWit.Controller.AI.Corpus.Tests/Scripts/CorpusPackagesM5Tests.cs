using System.Collections;
using System.Text.Json;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Scripts;

/// <summary>
/// Audit findings M-5 and H-8 through the engine: the bundled CorpusProcess.wit over six mock
/// nodes, a job long enough that its shards travel several to a package. What the script holds of
/// a round and what a node is handed and returns are references; the tasks and the results are in
/// blobs; a package a node could not answer as a whole costs that package a repair and nothing
/// else; and a shard far above the size its manifest declares ends as a verdict on the shard,
/// by name, on every machine that meets it.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusPackagesM5Tests : CorpusScriptTestBase
{
    #region Constants

    private const string SHARD = "tiny.warc.wet.gz";
    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const int SHARDS = 1100;

    #endregion

    #region Fields

    private string m_directory = null!;
    private CorpusTestOrigin m_tiny = null!;
    private string m_process = null!;
    private string m_preflight = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_packages_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
        WarcSynth.WriteWetShard(Path.Combine(m_directory, SHARD), 4, 120, 3);
        m_tiny = new CorpusTestOrigin(m_directory);
        m_process = LoadScript("CorpusProcess.wit");
        m_preflight = LoadScript("CorpusPreflight.wit");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_tiny.Dispose();
        CorpusTestFolders.Delete(m_directory);
    }

    [TearDown]
    public void Honest()
    {
        NodesManager.Rewrite = null;
        NodesManager.Tamper = null;
        NodesManager.StartTogether = false;
    }

    #endregion

    #region M-5 Tests

    [Test]
    public async Task M5AJobPastTheMostPackagesARoundHasTravelsAsReferencesTest()
    {
        // 1,100 listings of one small shard: two shards to a package, 550 packages.
        var manifest = await UploadAsync(TinyManifest(SHARDS), "packages.json");
        var requests = NodesManager.Requests;
        var requestBytes = NodesManager.RequestBytes;
        var returnBytes = NodesManager.ReturnBytes;
        NodesManager.StartTogether = true;

        var job = Engine.Compile(m_process);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, Guid.Empty, new CorpusOptionsData { SamplingRate = 0.01 });
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(status));

        var packages = ((IEnumerable)job.Variables["tasks"].Value!).Cast<object?>().ToList();
        var answers = ((IEnumerable)job.Variables["executions"].Value!).Cast<object?>().ToList();
        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        using var integrity = await ReadJsonAsync((Guid)job.Variables["integrity"].Value!);
        var shards = report.RootElement.GetProperty("shards").EnumerateArray().ToList();
        var sampled = CorpusPackagePlanner.Packages(SHARDS) / 100 + 1;
        var sent = NodesManager.Requests - requests;

        TestContext.Out.WriteLine($"{SHARDS} shards: {packages.Count} task packages, {answers.Count} answers; {sent} requests carried {NodesManager.RequestBytes - requestBytes} bytes of Corpus values to the nodes ({NodesManager.LargestRequestBytes} at the most in one) and {NodesManager.ReturnBytes - returnBytes} bytes came back as return values");

        Assert.Multiple(() =>
        {
            Assert.That(packages, Has.Count.EqualTo(550), "two shards to a package");
            Assert.That(packages, Is.All.InstanceOf<CorpusTaskPackageData>(), "what the script holds of the round is references");
            Assert.That(packages.Cast<CorpusTaskPackageData>().Select(package => package.ShardCount), Is.All.EqualTo(2));
            Assert.That(packages.Cast<CorpusTaskPackageData>().Sum(package => (long)MemoryPackSerializer.Serialize(package).Length), Is.LessThan(550 * 300), "a few hundred bytes a package");
            Assert.That(answers, Has.Count.EqualTo(550 + sampled), "an answer for each package, and one more for each sampled one");
            Assert.That(answers, Is.All.InstanceOf<CorpusResultPackageData>());
            Assert.That(sent, Is.EqualTo(550 + sampled), "a request a package: the nodes were never handed a shard as a parameter");
            Assert.That(NodesManager.LargestRequestBytes, Is.LessThan(400), "the most a node was handed for one task: a reference");
            Assert.That(TasksOf(packages).Select(task => task.TaskIndex), Is.EqualTo(Enumerable.Range(0, SHARDS)), "every shard of the manifest is in a package, in order");

            Assert.That(shards, Has.Count.EqualTo(SHARDS));
            Assert.That(shards.Select(shard => shard.GetProperty("index").GetInt32()), Is.EqualTo(Enumerable.Range(0, SHARDS)));
            Assert.That(shards.Select(shard => shard.GetProperty("verdict").GetString()), Is.All.EqualTo("Completed"));
            Assert.That(shards.Select(shard => shard.GetProperty("content_sha256").GetString()).Distinct().Count(), Is.EqualTo(1), "one shard listed 1,100 times: one product");
            Assert.That(shards.Count(shard => shard.GetProperty("executions").GetInt32() == 2), Is.EqualTo(2 * sampled), "a sampled package is two shards run twice");
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("sampled").GetInt32(), Is.EqualTo(2 * sampled));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("rejected_results").GetInt32(), Is.Zero);
            Assert.That(job.Variables["repairs"].Value as IEnumerable, Is.Empty);
            Assert.That(job.Variables["disputes"].Value as IEnumerable, Is.Empty);
        });
    }

    [Test]
    public async Task M5APackageANodeCouldNotAnswerIsRepairedAndNothingElseTest()
    {
        // The first three answers of the run come back as a fault of the node for the whole
        // package (its task blob did not arrive): six shards go out once more, under new tickets.
        var manifest = await UploadAsync(TinyManifest(SHARDS), "package-fault.json");
        var faulted = 0;
        NodesManager.Rewrite = (_, answer) => Interlocked.Increment(ref faulted) <= 3
            ? new CorpusResultPackageData { Ticket = answer.Ticket, Fault = CorpusResultCodes.FAULT_TASKS }
            : answer;

        var job = Engine.Compile(m_process);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, Guid.Empty, new CorpusOptionsData());
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(status));

        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        using var integrity = await ReadJsonAsync((Guid)job.Variables["integrity"].Value!);
        var shards = report.RootElement.GetProperty("shards").EnumerateArray().ToList();
        var repaired = shards.Where(shard => shard.TryGetProperty("faults", out _)).ToList();
        var repairs = ((IEnumerable)job.Variables["repairs"].Value!).Cast<CorpusTaskPackageData>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shards.Select(shard => shard.GetProperty("verdict").GetString()), Is.All.EqualTo("Completed"), "every shard has its result in the end");
            Assert.That(repaired, Has.Count.EqualTo(6), "three packages of two shards");
            Assert.That(repaired.SelectMany(shard => shard.GetProperty("faults").EnumerateArray().Select(fault => fault.GetString())), Is.All.EqualTo("tasks"));
            Assert.That(repairs.Sum(package => package.ShardCount), Is.EqualTo(6));
            Assert.That(repairs, Has.Count.EqualTo(6), "a repair round of six shards: a shard to a package");
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("repairs").GetInt32(), Is.EqualTo(6));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("node_faults").GetInt32(), Is.EqualTo(6));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("repaired").GetInt32(), Is.EqualTo(6));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("rejected_results").GetInt32(), Is.Zero, "a fault is no dropped value");
        });
    }

    [Test]
    public async Task M5AnAnswerThatNamesNothingTheServerHoldsCostsThatAnswerNotTheRunTest()
    {
        // A node answers two packages with a blob id of its own invention, and a third under a
        // ticket nobody gave out: the run has its report, and the shards are sent out once more.
        var manifest = await UploadAsync(TinyManifest(SHARDS), "package-forged.json");
        var forged = 0;
        NodesManager.Rewrite = (_, answer) => Interlocked.Increment(ref forged) switch
        {
            1 or 2 => new CorpusResultPackageData { Ticket = answer.Ticket, ResultsBlobId = Guid.NewGuid() },
            3 => new CorpusResultPackageData { Ticket = CorpusTickets.New(), ResultsBlobId = answer.ResultsBlobId },
            _ => answer
        };

        var job = Engine.Compile(m_process);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, Guid.Empty, new CorpusOptionsData());
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(status));

        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        using var integrity = await ReadJsonAsync((Guid)job.Variables["integrity"].Value!);
        var rejected = integrity.RootElement.GetProperty("rejected");

        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("tasks").GetInt32(), Is.EqualTo(SHARDS));
            Assert.That(report.RootElement.GetProperty("shards").EnumerateArray().Select(shard => shard.GetProperty("verdict").GetString()), Is.All.EqualTo("Completed"));
            Assert.That(rejected.GetProperty("unreadable").GetInt32(), Is.EqualTo(4), "two answers of two shards each");
            Assert.That(rejected.GetProperty("unknown_ticket").GetInt32(), Is.EqualTo(1), "an answer under no ticket of the round is one value, never opened");
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("repairs").GetInt32(), Is.EqualTo(6));
        });
    }

    #endregion

    #region H-8 Tests

    [Test]
    public async Task H8AShardFarAboveItsDeclaredSizeEndsOversizedByNameOnEveryMachineTest()
    {
        // An origin the server cannot ask for a size (it answers no HEAD), so the split has only
        // the manifest's word. The first source states its size; the second states one byte.
        var body = await File.ReadAllBytesAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WET_FIXTURE));
        using var origin = new CorpusRawOrigin(async (_, head, stream, cancellationToken) =>
        {
            if (head.StartsWith("HEAD", StringComparison.Ordinal))
            {
                await stream.WriteAsync(CorpusRawOrigin.Head("405 Method Not Allowed", "Content-Length: 0", "Connection: close"), cancellationToken);
                return;
            }

            await stream.WriteAsync(CorpusRawOrigin.Head("200 OK", $"Content-Length: {body.Length}", "Connection: close"), cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
        });
        var manifest = await UploadAsync($$"""
            {"pipeline":{"version":"h8","identify_language":false},"allowed_origins":["127.0.0.1"],
             "sources":[{"url":"{{origin.Url("honest.warc.wet.gz")}}","size":{{body.Length}}},{"url":"{{origin.Url("lying.warc.wet.gz")}}","size":1}]}
            """, "h8.json");

        var job = Engine.Compile(m_process);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, Guid.Empty, new CorpusOptionsData { SamplingRate = 1.0 });
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), Describe(status));

        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        using var integrity = await ReadJsonAsync((Guid)job.Variables["integrity"].Value!);
        var shards = report.RootElement.GetProperty("shards").EnumerateArray().ToList();
        var summary = integrity.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(shards[0].GetProperty("verdict").GetString(), Is.EqualTo("Completed"));
            Assert.That(shards[0].GetProperty("stats").GetProperty("bytes_fetched").GetInt64(), Is.EqualTo(body.Length));
            Assert.That(shards[1].GetProperty("verdict").GetString(), Is.EqualTo("Oversized"), "a verdict about the input");
            Assert.That(shards[1].GetProperty("error").GetString(), Is.EqualTo("oversized_declared"), "by name");
            Assert.That(shards[1].GetProperty("executions").GetInt32(), Is.EqualTo(2), "two machines met it and said the same");
            Assert.That(shards[1].TryGetProperty("faults", out _), Is.False, "it is no fault of a node");
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("repairs").GetInt32(), Is.Zero, "nothing is sent out again: every node would answer the same");
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.Zero);
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("bytes_fetched").GetInt64(), Is.EqualTo(body.Length), "the contributors fetched what the manifest promised");
        });
    }

    [Test]
    public async Task H8ASizeFarBelowTheOriginsStopsTheJobAtTheSplitByNameTest()
    {
        // The same lie where the server can ask: the preflight names it and the split refuses it,
        // before a node fetches a byte.
        var size = new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WET_FIXTURE)).Length;
        var manifest = await UploadAsync($$"""
            {"pipeline":{"version":"h8","identify_language":false},"allowed_origins":["{{Origin.Host}}"],
             "sources":[{"url":"{{Origin.ShardUrl(WET_FIXTURE)}}","size":1}]}
            """, "h8-probe.json");
        var problem = $"sources[0]: the origin answers {size} bytes and the manifest declares 1: a node holds the shard to {ShardLimits.DeclaredCap(1)} bytes and would refuse it (oversized_declared). Declare the shard's real size.";

        var preflight = Engine.Compile(m_preflight);
        await Engine.ScheduleAndWaitAsync(preflight, manifest, Guid.Empty, new CorpusOptionsData());
        var answer = (CorpusPreflightData)preflight.Variables["result"].Value!;

        var job = Engine.Compile(m_process);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, Guid.Empty, new CorpusOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(answer.WellFormed, Is.False);
            Assert.That(answer.Messages, Has.Some.EqualTo(problem));
            Assert.That(answer.Messages, Has.Some.Contains("1 declared size(s) checked, 1 of them far below the origin's"));
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(Describe(status), Does.Contain(problem));
            Assert.That(job.Variables["tasks"].Value as IEnumerable, Is.Null.Or.Empty, "no task was made");
        });
    }

    #endregion

    #region Tools

    private string TinyManifest(int shards)
    {
        var sources = string.Join(",", Enumerable.Repeat($"\"{m_tiny.ShardUrl(SHARD)}\"", shards));
        return $$"""{"pipeline":{"version":"packages","identify_language":false},"allowed_origins":["{{m_tiny.Host}}"],"sources":[{{sources}}]}""";
    }

    #endregion

    #region Properties

    /// <summary>Six nodes: more than the three every other rig has.</summary>
    protected override int Nodes => 6;

    #endregion
}
