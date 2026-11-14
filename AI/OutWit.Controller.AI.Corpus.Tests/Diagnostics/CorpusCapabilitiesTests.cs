using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Diagnostics;

/// <summary>
/// The capabilities document and the pool's totals: what an application can know before a
/// run, with no node named or told apart.
/// </summary>
[TestFixture]
public sealed class CorpusCapabilitiesTests
{
    #region Fields

    private CorpusRuntimeDiagnosticsData m_diagnostics = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_diagnostics = CorpusRuntimeDiagnosticsBuilder.Build(ShardOriginPolicy.Parse(null));
    }

    #endregion

    #region Pool Tests

    [Test]
    public void ThePoolIsAddedUpTest()
    {
        var summary = CorpusPoolProbe.Summarize([Measured(12.8, 32), Measured(7.2, 16), Measured(1.7, 4)]);

        Assert.That(summary.Is(new CorpusPoolSummary { Nodes = 3, Measured = 3, Workers = 52, Rate = 21.7 }), Is.True, summary.ToString());
    }

    [Test]
    public void ANodeThatHasNotMeasuredItselfCountsAsANodeOnlyTest()
    {
        var summary = CorpusPoolProbe.Summarize([Measured(10, 8), WitBenchmarkResult.Default, new WitBenchmarkResult { Rate = 99, Unit = "another-unit" }]);

        Assert.That(summary.Is(new CorpusPoolSummary { Nodes = 3, Measured = 1, Workers = 8, Rate = 10 }), Is.True, summary.ToString());
    }

    [Test]
    public void TheWorkersAreNotGuessedWhenTheHostDoesNotCarryThemTest()
    {
        // The cloud server keeps a benchmark's rate and unit, not its custom values: a total over
        // the nodes that happen to carry the degree would read as the pool's.
        var summary = CorpusPoolProbe.Summarize([Measured(10, 8), new WitBenchmarkResult { Rate = 5, Unit = CorpusBenchmark.UNIT }]);

        using var document = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(m_diagnostics, summary, CorpusStorageLimit.NONE));

        Assert.Multiple(() =>
        {
            Assert.That(summary.Workers, Is.Null);
            Assert.That(document.RootElement.GetProperty("pool").TryGetProperty("workers", out _), Is.False);
            Assert.That(document.RootElement.GetProperty("pool").GetProperty("rate").GetDouble(), Is.EqualTo(15));
        });
    }

    [Test]
    public void AProcessWithoutANodesManagerCannotSayTest()
    {
        Assert.That(CorpusPoolProbe.SummarizeAsync(null).GetAwaiter().GetResult(), Is.Null);
    }

    #endregion

    #region Document Tests

    [Test]
    public void TheDocumentSaysVersionStagesLimitsAndThePoolTest()
    {
        using var document = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(m_diagnostics, new CorpusPoolSummary { Nodes = 7, Measured = 6, Rate = 31.45678 }, CorpusStorageLimit.NONE));
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("corpus-capabilities/1"));
            Assert.That(root.GetProperty("controller").GetString(), Does.Match("^[0-9]+[.][0-9]+[.][0-9]+"));
            Assert.That(root.GetProperty("benchmark_unit").GetString(), Is.EqualTo(CorpusBenchmark.UNIT));
            Assert.That(root.GetProperty("stages").GetProperty("verified").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("stages").GetProperty("order").GetArrayLength(), Is.EqualTo(CorpusStageSet.STAGES.Count));
            Assert.That(root.GetProperty("stages").GetProperty("language_format").GetString(), Is.EqualTo("WITLID2"), "the language data the stage set reads");
            Assert.That(root.GetProperty("pipeline").GetProperty("switches").GetProperty("extract_pdf").GetBoolean(), Is.True, "the PDF stage is on unless a manifest turns it off");
            Assert.That(root.GetProperty("parsers").GetProperty("html").GetString(), Does.StartWith("AngleSharp "));
            Assert.That(root.GetProperty("language_model").GetProperty("reference_sha256").GetString(), Is.EqualTo(CorpusReferenceLanguageModel.SHA256));
            Assert.That(root.GetProperty("language_model").GetProperty("format").GetString(), Is.EqualTo("WITLID2"));
            Assert.That(root.GetProperty("language_model").GetProperty("formats").EnumerateArray().Select(format => format.GetString()), Is.EqualTo(new[] { "WITLID1", "WITLID2" }));
            Assert.That(root.GetProperty("language_model").GetProperty("unsupported_drop").GetString(), Is.EqualTo("language_unsupported"));
            Assert.That(root.GetProperty("language_model").GetProperty("reference_known_languages").GetArrayLength(), Is.EqualTo(CorpusReferenceLanguageModel.KNOWN_LANGUAGES.Count));
            Assert.That(root.GetProperty("language_model").GetProperty("witlid1_reference_sha256").GetString(), Is.EqualTo(CorpusReferenceLanguageModel.WITLID1_SHA256));
            Assert.That(root.GetProperty("pipeline").GetProperty("switches").GetProperty("filter_curly_bracket").GetBoolean(), Is.False, "C4's curly-bracket rule is off unless a manifest turns it on");
            Assert.That(root.GetProperty("formats").EnumerateArray().Select(format => format.GetString()), Is.EqualTo(new[] { "wet", "warc" }));
            Assert.That(root.GetProperty("origins").GetProperty("public")[0].GetString(), Is.EqualTo("data.commoncrawl.org"));
            Assert.That(root.GetProperty("origins").GetProperty("machine_variable").GetString(), Is.EqualTo(ShardOriginPolicy.EXTRA_ORIGINS_VARIABLE));
            Assert.That(root.GetProperty("defaults").GetProperty("max_shard_bytes").GetInt64(), Is.EqualTo(1L << 30));
            Assert.That(root.GetProperty("defaults").GetProperty("dedup_threshold").GetDouble(), Is.EqualTo(0.8));
            Assert.That(root.GetProperty("ceilings").GetProperty("html_nesting").GetInt32(), Is.EqualTo(512));
            Assert.That(root.GetProperty("signatures").GetProperty("rows_per_band").GetInt32(), Is.EqualTo(8));
            Assert.That(root.GetProperty("products").EnumerateArray().Select(product => product.GetProperty("name").GetString()), Is.EqualTo(new[] { "text", "signatures", "report", "integrity", "dedup" }));
            Assert.That(root.GetProperty("storage").GetProperty("ratios").GetArrayLength(), Is.EqualTo(CorpusStorageRatios.TABLE.Count));
            Assert.That(root.GetProperty("pool").GetProperty("nodes").GetInt32(), Is.EqualTo(7));
            Assert.That(root.GetProperty("pool").GetProperty("rate").GetDouble(), Is.EqualTo(31.457));
        });
    }

    [Test]
    public void TheStageParametersCarryTheStageDefaultsTest()
    {
        using var document = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(m_diagnostics, null, CorpusStorageLimit.NONE));
        var pipeline = document.RootElement.GetProperty("pipeline");
        var quality = pipeline.GetProperty("quality");

        Assert.Multiple(() =>
        {
            Assert.That(pipeline.GetProperty("switches").GetProperty("clean_lines").GetBoolean(), Is.EqualTo(new CorpusPipelineData().CleanLines));
            Assert.That(pipeline.GetProperty("switches").GetProperty("extract_pdf").GetBoolean(), Is.True, "the PDF stage is on unless a manifest turns it off");
            Assert.That(quality.GetProperty("min_words").GetProperty("spaced").GetDouble(), Is.EqualTo(QualityRules.Spaced.MinWords));
            Assert.That(quality.GetProperty("min_words").GetProperty("unspaced").GetDouble(), Is.EqualTo(QualityRules.Unspaced.MinWords));
            Assert.That(quality.GetProperty("max_duplicate_line_ratio").GetProperty("spaced").GetDouble(), Is.EqualTo(QualityRules.Spaced.MaxDuplicateLineRatio));
            Assert.That(quality.GetProperty("max_mean_word_length").GetProperty("unspaced").ValueKind, Is.EqualTo(JsonValueKind.Null), "a rule a script's base turns off has no number");
            Assert.That(pipeline.GetProperty("quality_fixed").GetProperty("duplicate_ngram_chars").GetArrayLength(), Is.EqualTo(QualityRules.Spaced.MaxDuplicateNgramCharRatio.Count));
        });
    }

    [Test]
    public void TheDocumentNamesThePdfStageItsModuleAndItsLimitsTest()
    {
        using var document = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(m_diagnostics, null, CorpusStorageLimit.NONE));
        var pdf = document.RootElement.GetProperty("pdf");

        Assert.Multiple(() =>
        {
            Assert.That(pdf.GetProperty("stage").GetString(), Is.EqualTo("extract_pdf"));
            Assert.That(pdf.GetProperty("module_sha256").GetString(), Is.EqualTo(PdfStageLimits.MODULE_SHA256));
            Assert.That(pdf.GetProperty("fuel").GetInt64(), Is.EqualTo(PdfStageLimits.FUEL));
            Assert.That(pdf.GetProperty("fuel_after_instantiation").GetBoolean(), Is.True);
            Assert.That(pdf.GetProperty("memory_bytes").GetInt64(), Is.EqualTo(1L << 30));
            Assert.That(pdf.GetProperty("input_bytes").GetInt64(), Is.EqualTo(64L << 20));
            Assert.That(pdf.GetProperty("text_bytes").GetInt64(), Is.EqualTo(16L << 20));
            Assert.That(pdf.GetProperty("document_words").GetInt32(), Is.EqualTo(5000));
            Assert.That(pdf.GetProperty("running_lines").GetProperty("min_pages").GetInt32(), Is.EqualTo(PdfStageLimits.RUNNING_LINE_MIN_PAGES));
            Assert.That(pdf.GetProperty("drops").EnumerateArray().Select(drop => drop.GetString()), Is.EquivalentTo(ShardPdfStage.DROPS));
            Assert.That(pdf.GetProperty("available").GetBoolean(), Is.EqualTo(m_diagnostics.PdfStageProblem.Length == 0));
        });
    }

    [Test]
    public void TheHeadroomAppearsOnlyWhenStatedTest()
    {
        using var unstated = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(m_diagnostics, null, CorpusStorageLimit.NONE));
        using var stated = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(m_diagnostics, null, new CorpusStorageLimit { HeadroomBytes = 50L << 30, RetentionDays = 7, Source = "x" }));

        Assert.Multiple(() =>
        {
            Assert.That(unstated.RootElement.GetProperty("storage").TryGetProperty("headroom_bytes", out _), Is.False);
            Assert.That(unstated.RootElement.GetProperty("storage").TryGetProperty("retention_days", out _), Is.False);
            Assert.That(stated.RootElement.GetProperty("storage").GetProperty("headroom_bytes").GetInt64(), Is.EqualTo(50L << 30));
            Assert.That(stated.RootElement.GetProperty("storage").GetProperty("retention_days").GetInt32(), Is.EqualTo(7));
        });
    }

    [Test]
    public void TheDocumentNamesTheOutputModesTheMinHashRangesAndTheDropStagesTest()
    {
        using var document = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(m_diagnostics, null, CorpusStorageLimit.NONE));
        var pipeline = document.RootElement.GetProperty("pipeline");
        var minHash = pipeline.GetProperty("minhash");
        var rules = document.RootElement.GetProperty("drop_rules").EnumerateArray().ToList();
        var products = document.RootElement.GetProperty("products").EnumerateArray().ToDictionary(
            product => product.GetProperty("name").GetString()!,
            product => product.GetProperty("outputs").EnumerateArray().Select(output => output.GetString()).ToList());

        Assert.Multiple(() =>
        {
            Assert.That(pipeline.GetProperty("output").GetProperty("default").GetString(), Is.EqualTo("full"));
            Assert.That(pipeline.GetProperty("output").GetProperty("values").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[] { "full", "stats_and_hashes" }));
            Assert.That(minHash.GetProperty("hashes").GetProperty("default").GetInt32(), Is.EqualTo(128));
            Assert.That(minHash.GetProperty("hashes").GetProperty("max").GetInt32(), Is.EqualTo(1024));
            Assert.That(minHash.GetProperty("shingle_words").GetProperty("default").GetInt32(), Is.EqualTo(5));
            Assert.That(minHash.GetProperty("shingle_words").GetProperty("max").GetInt32(), Is.EqualTo(32));
            Assert.That(minHash.GetProperty("bands").GetProperty("default").GetInt32(), Is.EqualTo(16));
            Assert.That(rules.Select(rule => rule.GetProperty("name").GetString()), Is.EqualTo(ShardDropRules.ALL.Select(rule => rule.Rule)));
            Assert.That(rules.Single(rule => rule.GetProperty("name").GetString() == "no_prose_lines").GetProperty("stage").GetString(), Is.EqualTo("lines"));
            Assert.That(rules.Select(rule => rule.GetProperty("name").GetString()), Is.SupersetOf(QualityFilter.RULES).And.SupersetOf(ShardPdfStage.DROPS));
            Assert.That(products["text"], Is.EqualTo(new[] { "full" }));
            Assert.That(products["signatures"], Is.EqualTo(new[] { "full" }));
            Assert.That(products["dedup"], Is.EqualTo(new[] { "full" }));
            Assert.That(products["report"], Is.EqualTo(new[] { "full", "stats_and_hashes" }));
            Assert.That(products["integrity"], Is.EqualTo(new[] { "full", "stats_and_hashes" }));
        });
    }

    [Test]
    public void AHostThatCannotSayLeavesThePoolOutTest()
    {
        using var document = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(m_diagnostics, null, CorpusStorageLimit.NONE));

        Assert.That(document.RootElement.TryGetProperty("pool", out _), Is.False);
    }

    [Test]
    public void TheDocumentNamesNoMachineTest()
    {
        var json = Encoding.UTF8.GetString(CorpusCapabilitiesWriter.Write(m_diagnostics, new CorpusPoolSummary { Nodes = 1, Measured = 1, Workers = 16, Rate = 12 }, CorpusStorageLimit.NONE));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain(Environment.MachineName));
            Assert.That(json, Does.Not.Contain(m_diagnostics.RuntimeTarget), "the host's own platform is not the pool's");
            Assert.That(json, Does.Not.Contain("node_id"));
        });
    }

    #endregion

    #region Tools

    private static IWitBenchmarkResult Measured(double rate, int degree)
    {
        return new WitBenchmarkResult
        {
            Rate = rate,
            Unit = CorpusBenchmark.UNIT,
            Custom = new Dictionary<string, string> { ["degree"] = degree.ToString() }
        };
    }

    #endregion
}
