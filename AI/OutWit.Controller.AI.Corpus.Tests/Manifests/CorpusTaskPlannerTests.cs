using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Manifests;

/// <summary>The planner: one task per source with the pipeline pinned, and every refusal the host makes so a node never has to.</summary>
[TestFixture]
public sealed class CorpusTaskPlannerTests
{
    #region Constants

    private const string MODEL_SHA = "9670e262a8453661e47f0a4e838e0970fff561e20b3865b383227b01ba25558f";

    #endregion

    #region Tests

    [Test]
    public void OneTaskPerSourceWithThePipelinePinnedTest()
    {
        var modelBlob = Guid.NewGuid();
        var plan = CorpusTaskPlanner.Plan(Manifest(sources: ["https://h/a.warc.wet.gz", "https://H/b.warc.gz"]), new CorpusOptionsData { MaxShardBytes = 500_000_000 }, modelBlob, MODEL_SHA);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Tasks, Has.Count.EqualTo(2));
            Assert.That(plan.Tasks.Select(t => t.TaskIndex), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(plan.Tasks[0].Source!.Format, Is.EqualTo(CorpusSourceFormat.Wet));
            Assert.That(plan.Tasks[1].Source!.Format, Is.EqualTo(CorpusSourceFormat.Warc));
            Assert.That(plan.Tasks[0].MaxShardBytes, Is.EqualTo(500_000_000));
            Assert.That(plan.Tasks[0].AllowedOrigins, Is.EqualTo(new[] { "h" }));
            Assert.That(plan.Tasks[0].Pipeline!.LanguageModelBlobId, Is.EqualTo(modelBlob));
            Assert.That(plan.Tasks[0].Pipeline!.LanguageModelSha256, Is.EqualTo(MODEL_SHA));
            Assert.That(ReferenceEquals(plan.Tasks[0].Pipeline, plan.Tasks[1].Pipeline), Is.False, "each task owns its pipeline copy");
            Assert.That(plan.EstimatedTrafficBytes, Is.Zero, "no sizes in the manifest");
        });
    }

    [Test]
    public void CapIsBoundedByTheControllerTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusTaskPlanner.ShardCap(null), Is.EqualTo(CorpusTaskPlanner.MAX_SHARD_BYTES));
            Assert.That(CorpusTaskPlanner.ShardCap(new CorpusOptionsData()), Is.EqualTo(CorpusTaskPlanner.MAX_SHARD_BYTES));
            Assert.That(CorpusTaskPlanner.ShardCap(new CorpusOptionsData { MaxShardBytes = 10 }), Is.EqualTo(10));
            Assert.That(CorpusTaskPlanner.ShardCap(new CorpusOptionsData { MaxShardBytes = long.MaxValue }), Is.EqualTo(CorpusTaskPlanner.MAX_SHARD_BYTES));
        });
    }

    [Test]
    public void RefusedOriginsStopThePlanTest()
    {
        var plan = CorpusTaskPlanner.Plan(Manifest(sources: ["https://h/a.warc.gz", "https://evil.example/b.warc.gz", "https://evil.example/c.warc.gz"]), null, Guid.NewGuid(), MODEL_SHA);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Is.Empty);
            Assert.That(plan.Errors, Has.Exactly(2).Contains("not an allowed origin"));
            Assert.That(plan.RefusedOrigins, Is.EqualTo(new[] { "evil.example" }));
        });
    }

    [Test]
    public void OversizedSourcesStopThePlanTest()
    {
        var manifest = Manifest(sources: ["https://h/a.warc.gz"]);
        manifest.Sources[0].SizeBytes = 2_000;
        var plan = CorpusTaskPlanner.Plan(manifest, new CorpusOptionsData { MaxShardBytes = 1_000 }, Guid.NewGuid(), MODEL_SHA);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Is.Empty);
            Assert.That(plan.OversizedSources, Is.EqualTo(1));
            Assert.That(plan.EstimatedTrafficBytes, Is.EqualTo(2_000));
            Assert.That(plan.Errors, Has.Some.Contains("exceeds the cap"));
        });
    }

    [Test]
    public void LanguageStageNeedsAModelTest()
    {
        var plan = CorpusTaskPlanner.Plan(Manifest(sources: ["https://h/a.warc.gz"]), null, Guid.Empty, "");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Is.Empty);
            Assert.That(plan.Errors, Has.Some.Contains("no language model was given"));
        });
    }

    [Test]
    public void PinnedModelMustMatchTest()
    {
        var manifest = Manifest(sources: ["https://h/a.warc.gz"]);
        manifest.Pipeline.LanguageModelSha256 = MODEL_SHA;

        var matching = CorpusTaskPlanner.Plan(manifest, null, Guid.NewGuid(), MODEL_SHA);
        var other = CorpusTaskPlanner.Plan(manifest, null, Guid.NewGuid(), new string('0', 64));

        Assert.Multiple(() =>
        {
            Assert.That(matching.Errors, Is.Empty);
            Assert.That(other.Tasks, Is.Empty);
            Assert.That(other.Errors, Has.Some.Contains("does not match the pinned"));
        });
    }

    [Test]
    public void AModelThisControllerCannotUseStopsThePlanTest()
    {
        var pack = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "language-pack-cc-2026-43-16.bin"));
        var manifest = Manifest(sources: ["https://h/a.warc.gz"]);

        var garbage = CorpusTaskPlanner.Plan(manifest, null, Guid.NewGuid(), MODEL_SHA, languageModel: CorpusLanguageModelFacts.Read(System.Text.Encoding.ASCII.GetBytes("not a model at all")));
        var truncated = CorpusTaskPlanner.Plan(manifest, null, Guid.NewGuid(), MODEL_SHA, languageModel: CorpusLanguageModelFacts.Read(pack[..(pack.Length / 2)]));
        var stageOff = Manifest(sources: ["https://h/a.warc.gz"]);
        stageOff.Pipeline.IdentifyLanguage = false;
        var off = CorpusTaskPlanner.Plan(stageOff, null, Guid.NewGuid(), MODEL_SHA, languageModel: CorpusLanguageModelFacts.Read([1, 2, 3]));

        Assert.Multiple(() =>
        {
            Assert.That(garbage.Tasks, Is.Empty);
            Assert.That(garbage.Errors, Has.Some.Contains("this controller cannot use the language model").And.Some.Contains("WITLID1 and WITLID2"));
            Assert.That(truncated.Tasks, Is.Empty);
            Assert.That(truncated.Errors, Has.Some.Contains("truncated"));
            Assert.That(off.Errors, Is.Empty, "a model the pipeline does not use is not judged");
        });
    }

    [Test]
    public void AUsableModelIsDescribedAndTargetsItLacksAreNamedTest()
    {
        var pack = CorpusLanguageModelFacts.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "language-pack-cc-2026-43-16.bin"));
        var witlid1 = CorpusLanguageModelFacts.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "language-model-cc-2026-43-16.bin"));
        var manifest = Manifest(sources: ["https://h/a.warc.gz"]);
        manifest.Pipeline.TargetLanguages = ["eng", "swe"];

        var withPack = CorpusTaskPlanner.Plan(manifest, null, Guid.NewGuid(), MODEL_SHA, languageModel: pack);
        var withWitlid1 = CorpusTaskPlanner.Plan(manifest, null, Guid.NewGuid(), MODEL_SHA, languageModel: witlid1);

        Assert.Multiple(() =>
        {
            Assert.That(pack.Problem, Is.Null);
            Assert.That(pack.KnownLanguages, Is.EqualTo(23));
            Assert.That(withPack.Errors, Is.Empty);
            Assert.That(withPack.Tasks, Has.Count.EqualTo(1));
            Assert.That(withPack.Notes, Has.Some.Contains("WITLID2 pack 'cc-2026-43-16', 16 supported language(s)"));
            Assert.That(withPack.Notes, Has.Some.Contains("target_languages: swe not among the model's languages"));
            Assert.That(withWitlid1.Notes, Has.Some.Contains("WITLID1, 16 language(s)"));
        });
    }

    [Test]
    public void LanguageStageOffNeedsNoModelTest()
    {
        var manifest = Manifest(sources: ["https://h/a.warc.gz"]);
        manifest.Pipeline.IdentifyLanguage = false;
        manifest.Pipeline.TargetLanguages = ["eng"];

        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.Empty, "");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Tasks[0].Pipeline!.LanguageModelBlobId, Is.EqualTo(Guid.Empty));
            Assert.That(plan.Notes, Has.Some.Contains("target_languages are ignored"));
        });
    }

    [Test]
    public void HostOutsideThePublicOriginsIsPlannedAndNamedTest()
    {
        // Whether to fetch from such a host is each machine's own decision, so the plan
        // stands and says which hosts the machines will be asked about.
        var plan = CorpusTaskPlanner.Plan(Manifest(sources: ["https://h/a.warc.gz", "https://H/b.warc.gz"]), null, Guid.NewGuid(), MODEL_SHA, new ShardOriginPolicy());

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Tasks, Has.Count.EqualTo(2));
            Assert.That(plan.OutsideOrigins, Is.EqualTo(new[] { "h" }));
            Assert.That(plan.Notes, Has.Some.Contains("not a public origin").And.Contains(ShardOriginPolicy.EXTRA_ORIGINS_VARIABLE));
        });
    }

    [Test]
    public void HostThePlanningMachinesOwnerListedIsNotNamedTest()
    {
        var plan = CorpusTaskPlanner.Plan(Manifest(sources: ["https://h/a.warc.gz"]), null, Guid.NewGuid(), MODEL_SHA, new ShardOriginPolicy(["h"]));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.OutsideOrigins, Is.Empty);
            Assert.That(plan.Notes, Has.None.Contains("not a public origin"));
        });
    }

    [Test]
    public void PublicOriginIsPlannedWithoutANoteTest()
    {
        var manifest = new CorpusManifest(new CorpusPipelineData { Version = "v" }, ["data.commoncrawl.org"],
            [new CorpusSourceData { Url = "https://data.commoncrawl.org/crawl-data/a.warc.wet.gz", Format = CorpusSourceFormat.Wet }], []);

        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.NewGuid(), MODEL_SHA, new ShardOriginPolicy());

        Assert.Multiple(() =>
        {
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Tasks, Has.Count.EqualTo(1));
            Assert.That(plan.OutsideOrigins, Is.Empty);
        });
    }

    [TestCase("http://data.commoncrawl.org/crawl-data/a.warc.wet.gz", "https only")]
    [TestCase("https://data.commoncrawl.org:8443/crawl-data/a.warc.wet.gz", "default port")]
    public void PublicOriginWrittenAnotherWayStopsThePlanTest(string url, string reason)
    {
        var manifest = new CorpusManifest(new CorpusPipelineData { Version = "v" }, ["data.commoncrawl.org"],
            [new CorpusSourceData { Url = url, Format = CorpusSourceFormat.Wet }], []);

        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.NewGuid(), MODEL_SHA, new ShardOriginPolicy());

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Is.Empty);
            Assert.That(plan.Errors, Has.Some.Contains(reason));
        });
    }

    [Test]
    public void ParserErrorsCarryIntoThePlanTest()
    {
        var manifest = CorpusManifestParser.Parse("""{"pipeline":{"version":"v"},"allowed_origins":["h"],"sources":["https://h/a.bin"]}""");
        var plan = CorpusTaskPlanner.Plan(manifest, null, Guid.NewGuid(), MODEL_SHA);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Tasks, Is.Empty);
            Assert.That(plan.Errors, Has.Some.Contains("cannot tell the format"));
        });
    }

    #endregion

    #region Tools

    private static CorpusManifest Manifest(IEnumerable<string> sources)
    {
        return new CorpusManifest(
            new CorpusPipelineData { Version = "v" },
            ["h"],
            sources.Select(url => new CorpusSourceData { Url = url, Format = CorpusManifestParser.FormatFromUrl(url) ?? CorpusSourceFormat.Warc }).ToList(),
            []);
    }

    #endregion
}
