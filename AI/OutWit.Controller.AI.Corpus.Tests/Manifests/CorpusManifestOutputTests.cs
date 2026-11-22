using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Tests.Manifests;

/// <summary>
/// The manifest's <c>output</c> and <c>minhash</c>: parsed into the pipeline, refused by name when
/// wrong, absent as before they existed; and the plan's notes say what a run keeps and whether its
/// signatures can be merged, which the preflight shows.
/// </summary>
[TestFixture]
public sealed class CorpusManifestOutputTests
{
    #region Constants

    private const string SOURCE = "https://data.commoncrawl.org/crawl-data/CC-MAIN-2026-43/segments/1/wet/CC-MAIN-00000.warc.wet.gz";

    #endregion

    #region Parser Tests

    [Test]
    public void AbsentKeysLeaveThePipelineAsBeforeTest()
    {
        var manifest = Parse("");

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Errors, Is.Empty);
            Assert.That(manifest.Pipeline.Output, Is.EqualTo(CorpusOutputMode.Full));
            Assert.That(manifest.Pipeline.MinHash, Is.Null);
            Assert.That(manifest.Pipeline.Is(new CorpusPipelineData { Version = "v" }), Is.True, "the pipeline a 0.2.x manifest gave");
        });
    }

    [TestCase("\"full\"", CorpusOutputMode.Full)]
    [TestCase("\"stats_and_hashes\"", CorpusOutputMode.StatsAndHashes)]
    public void OutputParsesTest(string value, CorpusOutputMode expected)
    {
        var manifest = Parse($", \"output\": {value}");

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Errors, Is.Empty);
            Assert.That(manifest.Pipeline.Output, Is.EqualTo(expected));
        });
    }

    [TestCase("\"text\"")]
    [TestCase("\"Full\"")]
    [TestCase("1")]
    [TestCase("true")]
    public void AnUnknownOutputIsRefusedByNameTest(string value)
    {
        var manifest = Parse($", \"output\": {value}");

        Assert.That(manifest.Errors, Is.EqualTo(new[] { $"pipeline: 'output' must be \"full\" or \"stats_and_hashes\", not {value}." }));
    }

    [Test]
    public void MinHashParsesTest()
    {
        var manifest = Parse(", \"minhash\": { \"hashes\": 256, \"shingle_words\": 7, \"bands\": 32 }");

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Errors, Is.Empty);
            Assert.That(manifest.Pipeline.MinHash!.Is(new CorpusMinHashData { Hashes = 256, ShingleWords = 7, Bands = 32 }), Is.True);
        });
    }

    [TestCase("{ \"hashes\": 2000 }", "pipeline.minhash: 'hashes' must be an integer from 1 to 1024")]
    [TestCase("{ \"shingle_words\": 40 }", "pipeline.minhash: 'shingle_words' must be an integer from 1 to 32")]
    [TestCase("{ \"bands\": 12 }", "pipeline.minhash: 'bands' (12) must divide 'hashes' (128)")]
    [TestCase("{ \"hashes\": 100 }", "pipeline.minhash: 'bands' (16, the default) must divide 'hashes' (100)")]
    [TestCase("{ \"hashes\": 1.5 }", "pipeline.minhash: 'hashes' must be an integer.")]
    [TestCase("{ \"rows\": 8 }", "pipeline.minhash: unknown key 'rows'.")]
    [TestCase("[128]", "pipeline.minhash: must be an object.")]
    public void BadMinHashIsRefusedByNameTest(string value, string expected)
    {
        var manifest = Parse($", \"minhash\": {value}");

        Assert.That(manifest.Errors, Has.Count.EqualTo(1).And.Some.StartsWith(expected));
    }

    #endregion

    #region Planner Tests

    [Test]
    public void TheStatsAndHashesPlanSaysWhatStaysAndThatDedupCannotRunTest()
    {
        var plan = CorpusTaskPlanner.Plan(Parse(", \"identify_language\": false, \"output\": \"stats_and_hashes\""), null, Guid.Empty, "");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Tasks.Single().Pipeline!.Output, Is.EqualTo(CorpusOutputMode.StatsAndHashes), "the mode travels with the task");
            Assert.That(plan.Notes, Has.Some.StartsWith("output stats_and_hashes:").And.Contains("stay on its node").And.Contains("Corpus.Dedup cannot run over this run"));
        });
    }

    [Test]
    public void APlanThatDoesNotSignSaysDedupCannotRunTest()
    {
        var plan = CorpusTaskPlanner.Plan(Parse(", \"identify_language\": false, \"sign\": false, \"minhash\": { \"hashes\": 64 }"), null, Guid.Empty, "");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Notes, Has.Some.StartsWith("sign is false").And.Contains("Corpus.Dedup cannot run"));
            Assert.That(plan.Notes, Has.Some.EqualTo("pipeline.minhash is ignored: the run makes no signatures."));
        });
    }

    [Test]
    public void OtherParametersAreNamedInThePlanTest()
    {
        var plan = CorpusTaskPlanner.Plan(Parse(", \"identify_language\": false, \"minhash\": { \"hashes\": 64, \"bands\": 8 }"), null, Guid.Empty, "");
        var defaults = CorpusTaskPlanner.Plan(Parse(", \"identify_language\": false, \"minhash\": { \"hashes\": 128, \"shingle_words\": 5, \"bands\": 16 }"), null, Guid.Empty, "");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Notes, Has.Some.EqualTo("signatures: 64 hashes over 5-word shingles, 8 bands of 8 rows (the defaults: 128 hashes over 5-word shingles, 16 bands of 8 rows); Corpus.Dedup merges them only with signatures made the same way."));
            Assert.That(defaults.Notes, Has.None.StartsWith("signatures:"), "the defaults written out are the defaults");
            Assert.That(plan.Tasks.Single().Pipeline!.MinHash!.Hashes, Is.EqualTo(64), "the parameters travel with the task");
        });
    }

    #endregion

    #region Tools

    private static CorpusManifest Parse(string pipelineTail)
    {
        return CorpusManifestParser.Parse($$"""{"pipeline":{"version":"v"{{pipelineTail}}},"allowed_origins":["data.commoncrawl.org"],"sources":["{{SOURCE}}"]}""");
    }

    #endregion
}
