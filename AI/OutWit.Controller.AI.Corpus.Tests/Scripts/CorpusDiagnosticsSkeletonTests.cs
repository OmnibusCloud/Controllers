using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Tests.Docs;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Scripts;

/// <summary>
/// Through the engine: CorpusRuntimeDiagnostics.wit returns the module's diagnostics and the
/// capabilities document; CorpusPreflight.wit reports the server storage estimate and refuses
/// a job over the headroom the host states; CorpusProcess.wit does not start such a job. The
/// rig's blob service states the headroom through the members proposed for the server's.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusDiagnosticsSkeletonTests : CorpusScriptTestBase
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string WARC_FIXTURE = "cc-main-2026-43-00000-head.warc.gz";
    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";

    #endregion

    #region Fields

    private string m_diagnosticsScript = null!;
    private string m_preflightScript = null!;
    private string m_processScript = null!;
    private Guid m_modelBlob;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_diagnosticsScript = LoadScript("CorpusRuntimeDiagnostics.wit");
        m_preflightScript = LoadScript("CorpusPreflight.wit");
        m_processScript = LoadScript("CorpusProcess.wit");
        m_modelBlob = await UploadFixtureAsync(MODEL_FIXTURE);
    }

    [TearDown]
    public void TearDown()
    {
        BlobService.JobStorageHeadroomBytes = 0;
        BlobService.JobBlobRetentionDays = 0;
    }

    #endregion

    #region Diagnostics Tests

    [Test]
    public async Task TheDiagnosticsScriptReturnsTheModuleAndTheCapabilitiesTest()
    {
        var job = Engine.Compile(m_diagnosticsScript);
        var status = await Engine.ScheduleAndWaitAsync(job);

        var diagnostics = (CorpusRuntimeDiagnosticsData)job.Variables["result"].Value!;
        var capabilitiesBlob = (Guid)job.Variables["capabilities"].Value!;
        using var capabilities = await ReadJsonAsync(capabilitiesBlob);
        using var schema = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(SolutionRoot, "AI", "OutWit.Controller.AI.Corpus", "docs", "corpus-capabilities.schema.json")));

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(diagnostics.StagesVerified, Is.True);
            Assert.That(diagnostics.Stages, Is.EqualTo(CorpusStageSet.STAGES));
            Assert.That(diagnostics.ReferenceLanguageModelSha256, Is.EqualTo(CorpusReferenceLanguageModel.SHA256));
            Assert.That(capabilities.RootElement.GetProperty("schema").GetString(), Is.EqualTo(CorpusCapabilitiesWriter.SCHEMA));
            Assert.That(capabilities.RootElement.GetProperty("controller").GetString(), Is.EqualTo(diagnostics.ControllerVersion));
            Assert.That(capabilities.RootElement.GetProperty("pool").GetProperty("nodes").GetInt32(), Is.EqualTo(3), "the rig's three nodes");
            Assert.That(capabilities.RootElement.GetProperty("pool").GetProperty("measured").GetInt32(), Is.Zero, "the rig's nodes carry no benchmark");
            Assert.That(CorpusSchemaCheck.Validate(schema.RootElement, capabilities.RootElement), Is.Empty);
        });
    }

    #endregion

    #region Storage Tests

    [Test]
    public async Task PreflightReportsTheStorageEstimateTest()
    {
        var manifest = await UploadAsync(Manifest(WET_FIXTURE, WARC_FIXTURE), "storage-ok.json");
        BlobService.JobBlobRetentionDays = 7;

        var job = Engine.Compile(m_preflightScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, m_modelBlob, new CorpusOptionsData { SamplingRate = 0.5 });
        var report = (CorpusPreflightData)job.Variables["result"].Value!;

        var shardBytes = Size(WET_FIXTURE) + Size(WARC_FIXTURE);
        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(report.WellFormed, Is.True, string.Join(" | ", report.Messages));
            Assert.That(report.EstimatedStorageBytes, Is.GreaterThan(Size(MODEL_FIXTURE)), "the products are on top of the inputs");
            Assert.That(report.EstimatedStorageBytes, Is.LessThan(shardBytes + Size(MODEL_FIXTURE)), "a cleaned, compressed product is smaller than its shards");
            Assert.That(report.StorageHeadroomBytes, Is.Zero, "the rig states no headroom");
            Assert.That(report.StorageRetentionDays, Is.EqualTo(7));
            Assert.That(report.Messages, Has.Some.StartsWith("Server storage estimate").And.Contains("second copies of 1 sampled shard(s)").And.Contains("states no headroom"));
        });
    }

    [Test]
    public async Task PreflightRefusesAJobOverTheHeadroomTest()
    {
        var manifest = await UploadAsync(Manifest(WET_FIXTURE), "storage-over.json");
        BlobService.JobStorageHeadroomBytes = 100_000;

        var job = Engine.Compile(m_preflightScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, m_modelBlob, new CorpusOptionsData());
        var report = (CorpusPreflightData)job.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), "preflight reports, it does not fail");
            Assert.That(report.WellFormed, Is.False);
            Assert.That(report.TaskCount, Is.EqualTo(1), "the manifest itself is fine");
            Assert.That(report.StorageHeadroomBytes, Is.EqualTo(100_000));
            Assert.That(report.EstimatedStorageBytes, Is.GreaterThan(100_000));
            Assert.That(report.Messages, Has.Some.StartsWith("Server storage: the job would leave about").And.Contains("BlobStorage:JobStorageHeadroomBytes"));
        });
    }

    [Test]
    public async Task AJobOverTheHeadroomDoesNotStartTest()
    {
        var manifest = await UploadAsync(Manifest(WET_FIXTURE), "storage-split.json");
        BlobService.JobStorageHeadroomBytes = 100_000;

        var job = Engine.Compile(m_processScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, m_modelBlob, new CorpusOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(Messages(status), Has.Some.Contains("Server storage: the job would leave about"));
            Assert.That(job.Variables["report"].Value, Is.Null.Or.EqualTo(Guid.Empty), "no shard ran, no report");
        });
    }

    [Test]
    public async Task AJobUnderTheHeadroomRunsAndItsShardsSayTheirPlatformTest()
    {
        var manifest = await UploadAsync(Manifest(WET_FIXTURE), "storage-under.json");
        BlobService.JobStorageHeadroomBytes = 1L << 30;

        var job = Engine.Compile(m_processScript);
        var status = await Engine.ScheduleAndWaitAsync(job, manifest, m_modelBlob, new CorpusOptionsData());
        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        var shard = report.RootElement.GetProperty("shards")[0];

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(shard.GetProperty("verdict").GetString(), Is.EqualTo("Completed"));
            Assert.That(shard.GetProperty("runtime_target").GetString(), Is.EqualTo(CorpusRuntimeDiagnosticsBuilder.RuntimeTarget()));
            Assert.That(shard.GetProperty("degree").GetInt32(), Is.GreaterThan(0));
        });
    }

    #endregion

    #region Tools

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
