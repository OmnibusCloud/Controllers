using System.Text.Json;
using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Scripts;

/// <summary>
/// The distributed gate: the bundled CorpusProcess.wit through the engine (Corpus.Split →
/// Grid.ForEach ⇒ Corpus.ProcessShard → Corpus.Collect, blob transport, mock in-process nodes,
/// a loopback origin) turns the golden shards into products whose content hashes equal a direct
/// run's — the Pattern U pipeline end to end; and CorpusPreflight.wit reports a manifest's health.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusProcessSkeletonTests : CorpusScriptTestBase
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string WARC_FIXTURE = "cc-main-2026-43-00000-head.warc.gz";
    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";

    #endregion

    #region Fields

    private string m_processScript = null!;
    private string m_preflightScript = null!;
    private Guid m_modelBlob;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_processScript = LoadScript("CorpusProcess.wit");
        m_preflightScript = LoadScript("CorpusPreflight.wit");
        m_modelBlob = await UploadFixtureAsync(MODEL_FIXTURE);
    }

    #endregion

    #region Tests

    [Test]
    public void BundledScriptsCompileTest()
    {
        var process = Engine.Compile(m_processScript);
        var preflight = Engine.Compile(m_preflightScript);

        Assert.Multiple(() =>
        {
            Assert.That(process.Activities.Count, Is.GreaterThan(0));
            Assert.That(process.Variables.Select(v => v.Name), Is.SupersetOf(new[] { "manifest", "languageModel", "opts", "tasks", "results", "report" }));
            Assert.That(preflight.Variables.Select(v => v.Name), Is.SupersetOf(new[] { "manifest", "languageModel", "opts", "result" }));
        });
    }

    [Test]
    public async Task GoldenShardsFlowThroughTheEngineTest()
    {
        var manifestBlob = await UploadAsync(Manifest(WET_FIXTURE, WARC_FIXTURE), "manifest.json");

        var job = Engine.Compile(m_processScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifestBlob, m_modelBlob, new CorpusOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        var shards = report.RootElement.GetProperty("shards").EnumerateArray().ToList();
        var summary = report.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(shards, Has.Count.EqualTo(2));
            Assert.That(shards.Select(s => s.GetProperty("verdict").GetString()), Is.All.EqualTo("Completed"));
            Assert.That(summary.GetProperty("verdicts").GetProperty("Completed").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("documents_out").GetInt64(), Is.GreaterThan(100));
            Assert.That(summary.GetProperty("languages").GetProperty("eng").GetInt64(), Is.GreaterThan(0));
        });

        // The identity: each shard's content hash through the engine equals a direct run's.
        var model = LanguageModel.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", MODEL_FIXTURE));
        foreach (var (fixture, format, index) in new[] { (WET_FIXTURE, CorpusSourceFormat.Wet, 0), (WARC_FIXTURE, CorpusSourceFormat.Warc, 1) })
        {
            using var shard = File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixture));
            var direct = new ShardProcessor(new CorpusPipelineData { Version = "e2e-v1" }, model).Process(shard, format, new MemoryStream(), new MemoryStream());

            var reported = shards.Single(s => s.GetProperty("index").GetInt32() == index);
            Assert.That(reported.GetProperty("content_sha256").GetString(), Is.EqualTo(direct.ContentSha256), $"shard {index}");

            var textBlob = Guid.Parse(reported.GetProperty("text_blob").GetString()!);
            var lines = CorpusCompressor.Decompress(await ReadBytesAsync(textBlob));
            Assert.That(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(lines)), Is.EqualTo(direct.ContentSha256));

            var signatures = ShardSignatureWriter.Read(await ReadBytesAsync(Guid.Parse(reported.GetProperty("signatures_blob").GetString()!)));
            Assert.That(signatures, Has.Count.EqualTo(direct.Stats.DocumentsOut));
        }
    }

    [Test]
    public async Task RefusedOriginFailsAtSplitTest()
    {
        var manifest = $$"""{"pipeline":{"version":"e2e-v1"},"allowed_origins":["data.commoncrawl.org"],"sources":["{{Origin.ShardUrl(WET_FIXTURE)}}"]}""";
        var manifestBlob = await UploadAsync(manifest, "manifest.json");
        var before = Origin.Requests;

        var job = Engine.Compile(m_processScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifestBlob, m_modelBlob, new CorpusOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(Origin.Requests, Is.EqualTo(before), "nothing is fetched for a refused manifest");
        });
    }

    [Test]
    public async Task PreflightReportsTheManifestsHealthTest()
    {
        var good = await UploadAsync(Manifest(WET_FIXTURE), "good.json");
        var bad = await UploadAsync($$$"""{"pipeline":{"version":"e2e-v1","quality":{"min_word":1}},"allowed_origins":["{{{Origin.Host}}}"],"sources":["https://evil.example/x.warc.gz","{{{Origin.ShardUrl(WET_FIXTURE)}}}"]}""", "bad.json");

        var goodJob = Engine.Compile(m_preflightScript);
        var goodStatus = await Engine.ScheduleAndWaitAsync(goodJob, good, m_modelBlob, new CorpusOptionsData());
        var goodReport = (CorpusPreflightData)goodJob.Variables["result"].Value!;

        var badJob = Engine.Compile(m_preflightScript);
        var badStatus = await Engine.ScheduleAndWaitAsync(badJob, bad, Guid.Empty, new CorpusOptionsData());
        var badReport = (CorpusPreflightData)badJob.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(goodStatus.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(goodReport.WellFormed, Is.True, string.Join(" | ", goodReport.Messages));
            Assert.That(goodReport.TaskCount, Is.EqualTo(1));

            Assert.That(badStatus.Result, Is.EqualTo(WitProcessingResult.Completed), "preflight reports, it does not fail");
            Assert.That(badReport.WellFormed, Is.False);
            Assert.That(badReport.TaskCount, Is.Zero);
            Assert.That(badReport.RefusedOrigins, Is.EqualTo(new[] { "evil.example" }));
            Assert.That(badReport.Messages, Has.Some.Contains("unknown key 'min_word'"));
            Assert.That(badReport.Messages, Has.Some.Contains("no language model was given"));
        });
    }

    #endregion
}
