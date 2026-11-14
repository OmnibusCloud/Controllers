using System.Text.Json;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Reports;

/// <summary>
/// The report's funnel and output: over the golden shards every drop rule belongs to a stage, the
/// documents extracted less the language, line and quality drops are the documents out, and the
/// summary says what the run kept and how its signatures were made; a run that kept statistics
/// and hashes only names no blob.
/// </summary>
[TestFixture]
public sealed class CorpusReportFunnelTests
{
    #region Constants

    private const string WET_FIXTURE = "cc-main-2026-43-00000-head.warc.wet.gz";
    private const string WARC_FIXTURE = "cc-main-2026-43-00000-head.warc.gz";
    private const string MODEL_FIXTURE = "language-pack-cc-2026-43-16.bin";

    #endregion

    #region Fields

    private LanguageModel m_model = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_model = LanguageModel.Load(Path.Combine(Fixtures(), MODEL_FIXTURE));
    }

    #endregion

    #region Tests

    [Test]
    public void TheFunnelAddsUpOverTheGoldenShardsTest()
    {
        // The target languages make the language stage drop as well; the PDF stage is off so the
        // test runs without the extractor module.
        var pipeline = new CorpusPipelineData { Version = "test-v1", ExtractPdf = false, TargetLanguages = ["eng", "deu", "rus"] };
        var results = new[] { Result(0, WET_FIXTURE, CorpusSourceFormat.Wet, pipeline), Result(1, WARC_FIXTURE, CorpusSourceFormat.Warc, pipeline) };

        using var report = JsonDocument.Parse(CorpusReportWriter.Write(results));
        var summary = report.RootElement.GetProperty("summary");
        var funnel = summary.GetProperty("funnel");
        var dropped = funnel.GetProperty("dropped");
        var drops = summary.GetProperty("drops").EnumerateObject().ToList();

        long Dropped(string stage) => drops.Where(drop => ShardDropRules.StageOf(drop.Name) == stage).Sum(drop => drop.Value.GetInt64());

        Assert.Multiple(() =>
        {
            Assert.That(drops.Select(drop => drop.Name), Is.SubsetOf(ShardDropRules.ALL.Select(rule => rule.Rule)), "every drop the golden shards make is a rule with a stage");
            Assert.That(funnel.GetProperty("records").GetInt64(), Is.EqualTo(summary.GetProperty("records_in").GetInt64()));
            Assert.That(funnel.GetProperty("extracted").GetInt64(), Is.EqualTo(summary.GetProperty("documents_extracted").GetInt64()));
            Assert.That(funnel.GetProperty("out").GetInt64(), Is.EqualTo(summary.GetProperty("documents_out").GetInt64()));
            Assert.That(funnel.GetProperty("quality").GetInt64(), Is.EqualTo(funnel.GetProperty("out").GetInt64()), "nothing leaves a document after the quality filter");
            Assert.That(funnel.GetProperty("language").GetInt64(), Is.EqualTo(funnel.GetProperty("extracted").GetInt64() - dropped.GetProperty("language").GetInt64()));
            Assert.That(funnel.GetProperty("lines").GetInt64(), Is.EqualTo(funnel.GetProperty("language").GetInt64() - dropped.GetProperty("lines").GetInt64()));
            Assert.That(dropped.GetProperty("extract").GetInt64(), Is.EqualTo(Dropped(ShardDropRules.STAGE_EXTRACT)).And.GreaterThan(0), "the WARC shard's responses that are no page");
            Assert.That(dropped.GetProperty("language").GetInt64(), Is.EqualTo(Dropped(ShardDropRules.STAGE_LANGUAGE)).And.GreaterThan(0));
            Assert.That(dropped.GetProperty("lines").GetInt64(), Is.EqualTo(Dropped(ShardDropRules.STAGE_LINES)).And.GreaterThan(0));
            Assert.That(dropped.GetProperty("quality").GetInt64(), Is.EqualTo(Dropped(ShardDropRules.STAGE_QUALITY)).And.GreaterThan(0));
            Assert.That(summary.GetProperty("output").GetString(), Is.EqualTo("full"));
            Assert.That(summary.GetProperty("minhash").GetProperty("hashes").GetInt32(), Is.EqualTo(128));
            Assert.That(report.RootElement.GetProperty("shards")[0].GetProperty("minhash").GetProperty("bands").GetInt32(), Is.EqualTo(16));
        });
    }

    [Test]
    public void AStatsAndHashesReportNamesNoBlobTest()
    {
        var pipeline = new CorpusPipelineData { Version = "test-v1", Output = CorpusOutputMode.StatsAndHashes };
        var result = Result(0, WET_FIXTURE, CorpusSourceFormat.Wet, pipeline);

        using var report = JsonDocument.Parse(CorpusReportWriter.Write([result]));
        var summary = report.RootElement.GetProperty("summary");
        var shard = report.RootElement.GetProperty("shards")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("output").GetString(), Is.EqualTo("stats_and_hashes"));
            Assert.That(summary.TryGetProperty("minhash", out _), Is.False, "no signatures, no parameters");
            Assert.That(shard.GetProperty("text_blob").GetString(), Is.Empty);
            Assert.That(shard.GetProperty("signatures_blob").GetString(), Is.Empty);
            Assert.That(shard.GetProperty("content_sha256").GetString(), Is.EqualTo("5fd9af0b09764d3b017cf15c1338b56271faf9f0e857efa653c4dc71a2d3e74b"), "the golden WET product's hash, though the product is gone");
            Assert.That(shard.TryGetProperty("minhash", out _), Is.False);
            Assert.That(summary.GetProperty("funnel").GetProperty("out").GetInt64(), Is.EqualTo(summary.GetProperty("documents_out").GetInt64()));
        });
    }

    [Test]
    public void EveryRuleHasOneStageInTheOrderADocumentMeetsThemTest()
    {
        var stages = ShardDropRules.ALL.Select(rule => ShardDropRules.STAGES.ToList().IndexOf(rule.Stage)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ShardDropRules.ALL.Select(rule => rule.Rule), Is.Unique);
            Assert.That(stages, Is.Ordered, "extract, language, lines, quality");
            Assert.That(ShardDropRules.ALL.Select(rule => rule.Rule), Is.SupersetOf(ShardPdfStage.DROPS).And.SupersetOf(QualityFilter.RULES));
            Assert.That(ShardDropRules.StageOf("pdf_fuel"), Is.EqualTo(ShardDropRules.STAGE_EXTRACT));
            Assert.That(ShardDropRules.StageOf("language_unsupported"), Is.EqualTo(ShardDropRules.STAGE_LANGUAGE));
            Assert.That(ShardDropRules.StageOf("curly_bracket"), Is.EqualTo(ShardDropRules.STAGE_LINES));
            Assert.That(ShardDropRules.StageOf("duplicate_ngram_chars_7"), Is.EqualTo(ShardDropRules.STAGE_QUALITY));
            Assert.That(ShardDropRules.StageOf("a_rule_of_a_newer_controller"), Is.EqualTo(ShardDropRules.STAGE_QUALITY));
        });
    }

    #endregion

    #region Tools

    private CorpusResultData Result(int index, string fixture, CorpusSourceFormat format, CorpusPipelineData pipeline)
    {
        if (format == CorpusSourceFormat.Warc && pipeline.ExtractPdf)
            PdfTestModule.Require();

        var processor = new ShardProcessor(pipeline, m_model);
        using var shard = File.OpenRead(Path.Combine(Fixtures(), fixture));
        using var text = new MemoryStream();
        var product = processor.Process(shard, format, text, new MemoryStream());
        var signs = ShardProcessor.Signs(pipeline);

        return new CorpusResultData
        {
            TaskIndex = index,
            Verdict = CorpusShardVerdict.Completed,
            TextBlobId = pipeline.Output == CorpusOutputMode.Full ? Guid.NewGuid() : Guid.Empty,
            SignaturesBlobId = signs ? Guid.NewGuid() : Guid.Empty,
            ContentSha256 = product.ContentSha256,
            Stats = product.Stats.Build(1, text.Length, 0, 0),
            ControllerVersion = "test",
            Output = pipeline.Output,
            MinHash = signs ? MinHashParameters.Resolve(pipeline.MinHash).ToData() : null
        };
    }

    private static string Fixtures()
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");
    }

    #endregion
}
