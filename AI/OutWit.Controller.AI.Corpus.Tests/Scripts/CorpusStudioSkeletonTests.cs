using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Scripts;

/// <summary>
/// What the studio reads, through the engine: the preflight's busiest share on the rig's pool (a
/// share and counts, never a node); a run that keeps statistics and hashes only — its report names
/// no blob and carries the direct run's content hashes, its sampled pairs still compare, and
/// CorpusDedup refuses it by name; a run with its own MinHash parameters, which the report states
/// and the merge uses.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusStudioSkeletonTests : CorpusScriptTestBase
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string WARC_FIXTURE = "cc-main-2026-43-00000-head.warc.gz";
    private const string MODEL_FIXTURE = "language-pack-cc-2026-43-16.bin";

    #endregion

    #region Fields

    private string m_processScript = null!;
    private string m_preflightScript = null!;
    private string m_dedupScript = null!;
    private Guid m_modelBlob;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_processScript = LoadScript("CorpusProcess.wit");
        m_preflightScript = LoadScript("CorpusPreflight.wit");
        m_dedupScript = LoadScript("CorpusDedup.wit");
        m_modelBlob = await UploadFixtureAsync(MODEL_FIXTURE);
    }

    #endregion

    #region Preflight Tests

    [Test]
    public async Task PreflightStatesTheBusiestShareOfTheRigsPoolTest()
    {
        var manifest = await UploadAsync(Manifest(WET_FIXTURE, WARC_FIXTURE), "share.json");

        var job = Engine.Compile(m_preflightScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, m_modelBlob, new CorpusOptionsData());
        var report = (CorpusPreflightData)job.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(report.PlannedNodes, Is.EqualTo(2), "two shards of a similar size on the rig's three unmeasured nodes");
            Assert.That(report.BusiestNodeShare, Is.InRange(0.5, 0.6), "the larger of the two golden shards");
            Assert.That(report.Messages, Has.Some.StartsWith("Work split as the platform would plan it now: 2 of 3 node(s) take shards; the busiest node's share of the work is"));
        });
    }

    [Test]
    public async Task PreflightOfAStatsAndHashesRunSaysWhatStaysTest()
    {
        var manifest = await UploadAsync(StudioManifest(", \"output\": \"stats_and_hashes\"", WET_FIXTURE), "stats-preflight.json");

        var job = Engine.Compile(m_preflightScript);
        await Engine.ScheduleAndWaitAsync(job, manifest, m_modelBlob, new CorpusOptionsData { SamplingRate = 1 });
        var report = (CorpusPreflightData)job.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(report.WellFormed, Is.True, string.Join(" | ", report.Messages));
            Assert.That(report.Messages, Has.Some.StartsWith("output stats_and_hashes:").And.Contains("Corpus.Dedup cannot run over this run"));
            Assert.That(report.Messages, Has.Some.Contains("no text and no signatures (output stats_and_hashes: they stay on the nodes)"));
            Assert.That(report.EstimatedStorageBytes, Is.LessThan(Size(MODEL_FIXTURE) + 64 * 1024), "the report, the integrity document and the inputs");
        });
    }

    #endregion

    #region Run Tests

    [Test]
    public async Task AStatsAndHashesRunKeepsNoTextAndDedupRefusesItTest()
    {
        var manifest = await UploadAsync(StudioManifest(", \"output\": \"stats_and_hashes\"", WET_FIXTURE, WARC_FIXTURE), "stats.json");

        var job = Engine.Compile(m_processScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, m_modelBlob, new CorpusOptionsData { SamplingRate = 1.0 });
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        var reportBlob = (Guid)job.Variables["report"].Value!;
        using var report = await ReadJsonAsync(reportBlob);
        using var integrity = await ReadJsonAsync((Guid)job.Variables["integrity"].Value!);
        var shards = report.RootElement.GetProperty("shards").EnumerateArray().ToList();
        var model = LanguageModel.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", MODEL_FIXTURE));

        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("output").GetString(), Is.EqualTo("stats_and_hashes"));
            Assert.That(shards.Select(shard => shard.GetProperty("verdict").GetString()), Is.All.EqualTo("Completed"));
            Assert.That(shards.Select(shard => shard.GetProperty("text_blob").GetString()), Is.All.Empty);
            Assert.That(shards.Select(shard => shard.GetProperty("signatures_blob").GetString()), Is.All.Empty);
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("compared").GetInt32(), Is.EqualTo(2), "the hashes are still compared across machines");
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("mismatches").GetInt32(), Is.Zero);
        });

        foreach (var (fixture, format, index) in new[] { (WET_FIXTURE, CorpusSourceFormat.Wet, 0), (WARC_FIXTURE, CorpusSourceFormat.Warc, 1) })
        {
            using var shard = File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixture));
            var direct = new ShardProcessor(new CorpusPipelineData { Version = "e2e-v1" }, model).Process(shard, format, new MemoryStream(), new MemoryStream());
            Assert.That(shards.Single(s => s.GetProperty("index").GetInt32() == index).GetProperty("content_sha256").GetString(), Is.EqualTo(direct.ContentSha256), $"shard {index}: the full run's hash");
        }

        var dedup = Engine.Compile(m_dedupScript);
        var dedupStatus = await Engine.ScheduleAndWaitAsync(dedup, reportBlob, new CorpusOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(dedupStatus.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(Messages(dedupStatus), Has.Some.Contains("The run kept statistics and hashes only"));
        });
    }

    [Test]
    public async Task ARunWithItsOwnParametersIsMergedWithThemTest()
    {
        var manifest = await UploadAsync(StudioManifest(", \"minhash\": { \"hashes\": 64, \"bands\": 8 }", WET_FIXTURE), "minhash.json");

        var job = Engine.Compile(m_processScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, m_modelBlob, new CorpusOptionsData());
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        var reportBlob = (Guid)job.Variables["report"].Value!;
        using var report = await ReadJsonAsync(reportBlob);
        var shard = report.RootElement.GetProperty("shards")[0];
        var signatures = await ReadBytesAsync(Guid.Parse(shard.GetProperty("signatures_blob").GetString()!));

        var dedup = Engine.Compile(m_dedupScript);
        var dedupStatus = await Engine.ScheduleAndWaitAsync(dedup, reportBlob, new CorpusOptionsData());
        Assert.That(dedupStatus.Result, Is.EqualTo(WitProcessingResult.Completed), dedupStatus.ToString());
        using var merged = await ReadJsonAsync((Guid)dedup.Variables["dedup"].Value!);
        var summary = merged.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("minhash").GetProperty("hashes").GetInt32(), Is.EqualTo(64));
            Assert.That(shard.GetProperty("minhash").GetProperty("bands").GetInt32(), Is.EqualTo(8));
            Assert.That(ShardSignatureWriter.ReadHeader(signatures), Is.EqualTo((64, 5, ShardSignatureWriter.HEADER_SIZE_PARAMETERS)), "a WITSIG2 blob states how it was made");
            Assert.That(summary.GetProperty("hashes").GetInt32(), Is.EqualTo(64));
            Assert.That(summary.GetProperty("bands").GetInt32(), Is.EqualTo(8));
            Assert.That(summary.GetProperty("documents").GetInt64(), Is.EqualTo(shard.GetProperty("stats").GetProperty("documents_out").GetInt64()));
        });
    }

    #endregion

    #region Tools

    private string StudioManifest(string pipelineTail, params string[] fixtures)
    {
        var sources = string.Join(", ", fixtures.Select(fixture => $"\"{Origin.ShardUrl(fixture)}\""));
        return $$"""{"pipeline":{"version":"e2e-v1"{{pipelineTail}}},"allowed_origins":["{{Origin.Host}}"],"sources":[{{sources}}]}""";
    }

    private static long Size(string fixture)
    {
        return new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixture)).Length;
    }

    private static IEnumerable<string> Messages(IWitProcessingStatus status)
    {
        if (!string.IsNullOrEmpty(status.Message))
            yield return status.Message;

        foreach (var message in status.Children.SelectMany(Messages))
            yield return message;
    }

    #endregion
}
