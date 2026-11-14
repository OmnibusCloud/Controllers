using System.Text.Json;
using OutWit.Controller.AI.Corpus.Compression;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
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

    #region Tests

    [Test]
    public void BundledScriptsCompileTest()
    {
        var process = Engine.Compile(m_processScript);
        var preflight = Engine.Compile(m_preflightScript);

        Assert.Multiple(() =>
        {
            Assert.That(process.Activities.Count, Is.GreaterThan(0));
            Assert.That(process.Variables.Select(v => v.Name), Is.SupersetOf(new[] { "manifest", "languageModel", "opts", "tasks", "rate", "checked", "executions", "report", "integrity" }));
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

        // Split asked the origin for the sizes the manifest did not give: the work estimate is the shard's real size.
        var tasks = ((System.Collections.IEnumerable)job.Variables["tasks"].Value!).OfType<CorpusTaskData>().OrderBy(t => t.TaskIndex).ToList();
        Assert.That(tasks.Select(t => t.Source!.SizeBytes), Is.EqualTo(new[]
        {
            new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WET_FIXTURE)).Length,
            new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WARC_FIXTURE)).Length
        }));

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
    public async Task SamplingReExecutesAndComparesTest()
    {
        // Every shard executed twice (rate 1): each pair agrees on the content hash.
        var manifestBlob = await UploadAsync(Manifest(WET_FIXTURE, WARC_FIXTURE), "manifest.json");

        var job = Engine.Compile(m_processScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifestBlob, m_modelBlob, new CorpusOptionsData { SamplingRate = 1.0 });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        using var integrity = await ReadJsonAsync((Guid)job.Variables["integrity"].Value!);
        var summary = integrity.RootElement.GetProperty("summary");
        var pairs = integrity.RootElement.GetProperty("pairs").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("version_skew").GetInt32(), Is.Zero, "one controller version on every mock node");
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(pairs.Select(p => p.GetProperty("index").GetInt32()), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(pairs.All(p => p.GetProperty("primary").GetProperty("content_sha256").GetString() == p.GetProperty("replica").GetProperty("content_sha256").GetString()), Is.True);
        });

        // Four executions, two shards: the report still has one entry for each.
        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        Assert.That(report.RootElement.GetProperty("shards").GetArrayLength(), Is.EqualTo(2));
        Assert.That(((System.Collections.IEnumerable)job.Variables["executions"].Value!).OfType<CorpusResultData>().Count(), Is.EqualTo(4));

        // Rate 0 (the default) leaves the integrity document empty.
        var plain = Engine.Compile(m_processScript);
        var plainStatus = await Engine.ScheduleAndWaitAsync(plain, manifestBlob, m_modelBlob, new CorpusOptionsData());
        using var empty = await ReadJsonAsync((Guid)plain.Variables["integrity"].Value!);

        Assert.Multiple(() =>
        {
            Assert.That(plainStatus.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(empty.RootElement.GetProperty("summary").GetProperty("sampled").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task DedupOverTheReportFindsTheRepeatedShardTest()
    {
        // The same shard listed twice: every document of the second copy is a duplicate of the
        // first's; the WET shard alone has a few near-duplicates of its own, if any.
        var manifestBlob = await UploadAsync(Manifest(WET_FIXTURE, WET_FIXTURE), "manifest.json");
        var process = Engine.Compile(m_processScript);
        var processStatus = await Engine.ScheduleAndWaitAsync(process, manifestBlob, m_modelBlob, new CorpusOptionsData());
        Assert.That(processStatus.Result, Is.EqualTo(WitProcessingResult.Completed), processStatus.ToString());

        var reportBlob = (Guid)process.Variables["report"].Value!;
        var dedup = Engine.Compile(m_dedupScript);
        var dedupStatus = await Engine.ScheduleAndWaitAsync(dedup, reportBlob, new CorpusOptionsData());
        Assert.That(dedupStatus.Result, Is.EqualTo(WitProcessingResult.Completed), dedupStatus.ToString());

        using var report = await ReadJsonAsync(reportBlob);
        var documentsOut = report.RootElement.GetProperty("summary").GetProperty("documents_out").GetInt64();
        var perShard = documentsOut / 2;

        using var document = await ReadJsonAsync((Guid)dedup.Variables["dedup"].Value!);
        var summary = document.RootElement.GetProperty("summary");
        var shards = document.RootElement.GetProperty("shards").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo("corpus-dedup/1"));
            Assert.That(summary.GetProperty("documents").GetInt64(), Is.EqualTo(documentsOut));
            Assert.That(summary.GetProperty("duplicates").GetInt64(), Is.GreaterThanOrEqualTo(perShard), "the second copy is all duplicates");
            Assert.That(summary.GetProperty("duplicates").GetInt64(), Is.LessThan(documentsOut), "the first copy's representatives stay");
            Assert.That(shards, Has.Count.EqualTo(2));
            Assert.That(shards[1].GetProperty("duplicates").GetInt64(), Is.EqualTo(perShard), "every document of the repeated shard is dropped");
            Assert.That(shards[0].GetProperty("documents").GetInt64(), Is.EqualTo(perShard));
            Assert.That(summary.GetProperty("clusters").GetInt64(), Is.GreaterThanOrEqualTo(perShard - shards[0].GetProperty("duplicates").GetInt64()));
        });
    }

    [Test]
    public async Task DedupStillReadsAReportOfThePreviousSchemaTest()
    {
        var manifestBlob = await UploadAsync(Manifest(WET_FIXTURE, WET_FIXTURE), "manifest.json");
        var process = Engine.Compile(m_processScript);
        var processStatus = await Engine.ScheduleAndWaitAsync(process, manifestBlob, m_modelBlob, new CorpusOptionsData());
        Assert.That(processStatus.Result, Is.EqualTo(WitProcessingResult.Completed), processStatus.ToString());

        var current = System.Text.Encoding.UTF8.GetString(await ReadBytesAsync((Guid)process.Variables["report"].Value!));
        var previousBlob = await UploadAsync(current.Replace(CorpusReportWriter.SCHEMA, CorpusReportWriter.PREVIOUS_SCHEMA), "previous-report.json");

        var dedup = Engine.Compile(m_dedupScript);
        var status = await Engine.ScheduleAndWaitAsync(dedup, previousBlob, new CorpusOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());
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
    public async Task ALanguageModelThisControllerCannotUseIsRefusedBeforeAnyWorkTest()
    {
        // A truncated pack: Split refuses the job before a task exists (no node ever answers
        // PipelineUnavailable for it), and Preflight names the reason.
        var pack = await File.ReadAllBytesAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", MODEL_FIXTURE));
        var broken = await BlobService.UploadBytesAsync(pack[..(pack.Length / 2)], "broken-pack.bin");
        var manifest = await UploadAsync(Manifest(WET_FIXTURE), "manifest.json");
        var before = Origin.Requests;

        var process = Engine.Compile(m_processScript);
        var processStatus = await Engine.ScheduleAndWaitAsync(process, manifest, broken, new CorpusOptionsData());

        var preflight = Engine.Compile(m_preflightScript);
        var preflightStatus = await Engine.ScheduleAndWaitAsync(preflight, manifest, broken, new CorpusOptionsData());
        var report = (CorpusPreflightData)preflight.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(processStatus.Result, Is.EqualTo(WitProcessingResult.Failed), "Split refuses the job");
            Assert.That(Origin.Requests, Is.EqualTo(before), "nothing is fetched, not even a size");
            Assert.That(preflightStatus.Result, Is.EqualTo(WitProcessingResult.Completed), "preflight reports, it does not fail");
            Assert.That(report.WellFormed, Is.False);
            Assert.That(report.TaskCount, Is.Zero);
            Assert.That(report.Messages, Has.Some.Contains("this controller cannot use the language model").And.Some.Contains("truncated"));
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
            Assert.That(goodReport.EstimatedTrafficBytes, Is.EqualTo(new FileInfo(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", WET_FIXTURE)).Length), "a source without a size is probed at the origin");
            Assert.That(goodReport.Messages, Has.Some.Contains("1 source size(s) probed"));

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
