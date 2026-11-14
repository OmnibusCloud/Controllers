using System.Text.Json;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Tests.Docs;

/// <summary>
/// The published document vocabulary (docs/*.schema.json) stays in step with the parser and
/// the writers: every schema parses and names itself, the writers' output validates against
/// its schema, and the capabilities document lists exactly the stage parameters a manifest
/// may set. A new field lands in the schema in the same change as in the code.
/// </summary>
[TestFixture]
public sealed class CorpusSchemaDocsTests
{
    #region Constants

    private static readonly string[] SCHEMAS =
    [
        "corpus-manifest.schema.json",
        "corpus-report.schema.json",
        "corpus-integrity.schema.json",
        "corpus-dedup.schema.json",
        "corpus-capabilities.schema.json"
    ];

    #endregion

    #region Schema Tests

    [TestCaseSource(nameof(SCHEMAS))]
    public void SchemaParsesAndNamesItselfTest(string fileName)
    {
        using var document = Load(fileName);
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("$schema").GetString(), Does.Contain("json-schema.org"));
            Assert.That(root.GetProperty("$id").GetString(), Does.EndWith(fileName));
            Assert.That(root.GetProperty("title").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("object"));
        });
    }

    #endregion

    #region Capabilities Tests

    [Test]
    public void TheCapabilitiesWriterOutputValidatesTest()
    {
        using var schema = Load("corpus-capabilities.schema.json");
        var diagnostics = CorpusRuntimeDiagnosticsBuilder.Build(ShardOriginPolicy.Parse(null));

        // Every optional part present: a pool with workers, a stated headroom and window.
        using var full = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(diagnostics, new CorpusPoolSummary { Nodes = 2, Measured = 2, Workers = 20, Rate = 14.5 }, new CorpusStorageLimit { HeadroomBytes = 1L << 40, RetentionDays = 7, Source = "x" }));
        // Every optional part absent.
        using var bare = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(diagnostics, null, CorpusStorageLimit.NONE));

        Assert.Multiple(() =>
        {
            Assert.That(CorpusSchemaCheck.Validate(schema.RootElement, full.RootElement), Is.Empty);
            Assert.That(CorpusSchemaCheck.Validate(schema.RootElement, bare.RootElement), Is.Empty);
            Assert.That(schema.RootElement.GetProperty("properties").GetProperty("schema").GetProperty("const").GetString(), Is.EqualTo(CorpusCapabilitiesWriter.SCHEMA));
            Assert.That(PropertyNames(full.RootElement), Is.EquivalentTo(PropertyNames(schema.RootElement.GetProperty("properties"))), "every property the schema names is written");
        });
    }

    [Test]
    public void TheCheckSeesADocumentThatBreaksTheSchemaTest()
    {
        // The check itself: a missing required field, an unknown one and a wrong type are named.
        using var schema = Load("corpus-capabilities.schema.json");
        using var broken = JsonDocument.Parse("""{"schema":"corpus-capabilities/1","controller":1,"surprise":true}""");

        var problems = CorpusSchemaCheck.Validate(schema.RootElement, broken.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.Contains("'stages' is required"));
            Assert.That(problems, Has.Some.Contains("$.surprise: not a property the schema allows"));
            Assert.That(problems, Has.Some.Contains("$.controller: Number is not"));
        });
    }

    [Test]
    public void TheCapabilitiesListTheManifestsStageParametersTest()
    {
        using var manifest = Load("corpus-manifest.schema.json");
        var pipeline = manifest.RootElement.GetProperty("properties").GetProperty("pipeline").GetProperty("properties");
        var switches = pipeline.EnumerateObject().Where(property => property.Value.TryGetProperty("type", out var type) && type.GetString() == "boolean").Select(property => property.Name);
        var quality = PropertyNames(pipeline.GetProperty("quality").GetProperty("properties"));
        var minHash = PropertyNames(pipeline.GetProperty("minhash").GetProperty("properties"));
        var outputs = pipeline.GetProperty("output").GetProperty("enum").EnumerateArray().Select(value => value.GetString());

        using var capabilities = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(CorpusRuntimeDiagnosticsBuilder.Build(ShardOriginPolicy.Parse(null)), null, CorpusStorageLimit.NONE));
        var written = capabilities.RootElement.GetProperty("pipeline");

        Assert.Multiple(() =>
        {
            Assert.That(PropertyNames(written.GetProperty("switches")), Is.EquivalentTo(switches), "a manifest switch the capabilities do not list, or the reverse");
            Assert.That(PropertyNames(written.GetProperty("quality")), Is.EquivalentTo(quality), "a manifest quality key the capabilities do not list, or the reverse");
            Assert.That(PropertyNames(written.GetProperty("minhash")), Is.EquivalentTo(minHash), "a manifest MinHash key the capabilities do not list, or the reverse");
            Assert.That(written.GetProperty("output").GetProperty("values").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(outputs), "the output modes of the manifest and the capabilities");
            Assert.That(written.GetProperty("output").GetProperty("default").GetString(), Is.EqualTo(pipeline.GetProperty("output").GetProperty("default").GetString()));
        });
    }

    #endregion

    #region Report Tests

    [Test]
    public void TheReportWriterOutputValidatesTest()
    {
        using var schema = Load("corpus-report.schema.json");
        var results = new List<CorpusResultData>
        {
            new()
            {
                TaskIndex = 0, Verdict = CorpusShardVerdict.Completed, TextBlobId = Guid.NewGuid(), SignaturesBlobId = Guid.NewGuid(),
                ContentSha256 = "aa", ControllerVersion = "0.1.13", SourceUrl = "https://data.commoncrawl.org/a.warc.wet.gz", RuntimeTarget = "linux-x64", Degree = 4,
                MinHash = new CorpusMinHashData { Hashes = 128, ShingleWords = 5, Bands = 16 },
                Stats = new CorpusShardStatsData { RecordsIn = 10, DocumentsExtracted = 9, DocumentsOut = 5, Drops = [new CorpusCountData { Name = "min_words", Count = 4 }], Languages = [new CorpusCountData { Name = "eng", Count = 5 }] }
            },
            new()
            {
                TaskIndex = 2, Verdict = CorpusShardVerdict.Completed, TextBlobId = Guid.NewGuid(), SignaturesBlobId = Guid.NewGuid(),
                ContentSha256 = "bb", ControllerVersion = "0.2.0", SourceUrl = "https://data.commoncrawl.org/a.warc.gz", RuntimeTarget = "windows-x64", Degree = 8,
                MinHash = new CorpusMinHashData { Hashes = 128, ShingleWords = 5, Bands = 16 },
                Stats = new CorpusShardStatsData
                {
                    RecordsIn = 30, DocumentsExtracted = 12, DocumentsOut = 6, Drops = [new CorpusCountData { Name = "pdf_encrypted", Count = 1 }], Languages = [new CorpusCountData { Name = "eng", Count = 6 }],
                    Pdf = new CorpusPdfStatsData { In = 3, Truncated = 1, Extracted = 2, Pages = 14, Documents = 3, PagesCut = 0, RunningLines = 20, Fuel = 123_456_789 }
                }
            },
            new() { TaskIndex = 1, Verdict = CorpusShardVerdict.OriginRefused, Error = "not a public origin", SourceUrl = "https://evil.example/x.warc.gz" }
        };

        using var report = JsonDocument.Parse(CorpusReportWriter.Write(results));
        var shard = report.RootElement.GetProperty("shards")[0];
        var withPdfs = report.RootElement.GetProperty("shards")[2];
        var shardStats = schema.RootElement.GetProperty("properties").GetProperty("shards").GetProperty("items").GetProperty("properties").GetProperty("stats").GetProperty("oneOf")[1].GetProperty("properties");

        Assert.Multiple(() =>
        {
            Assert.That(CorpusSchemaCheck.Validate(schema.RootElement, report.RootElement), Is.Empty);
            Assert.That(shard.GetProperty("runtime_target").GetString(), Is.EqualTo("linux-x64"));
            Assert.That(shard.GetProperty("degree").GetInt32(), Is.EqualTo(4));
            Assert.That(PropertyNames(shard), Is.SubsetOf(PropertyNames(schema.RootElement.GetProperty("properties").GetProperty("shards").GetProperty("items").GetProperty("properties"))));
            Assert.That(PropertyNames(withPdfs.GetProperty("stats")), Is.EquivalentTo(PropertyNames(shardStats)), "every stats property the schema names is written for a shard with PDFs");
            Assert.That(PropertyNames(report.RootElement.GetProperty("summary")), Is.EquivalentTo(PropertyNames(schema.RootElement.GetProperty("properties").GetProperty("summary").GetProperty("properties"))));
        });
    }

    #endregion

    #region Tools

    private static JsonDocument Load(string fileName)
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "OutWit.slnx")))
            dir = Path.GetDirectoryName(dir);

        if (dir == null)
            Assert.Ignore("Solution root not found");

        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!, "AI", "OutWit.Controller.AI.Corpus", "docs", fileName)));
    }

    private static IReadOnlyList<string> PropertyNames(JsonElement element)
    {
        return element.EnumerateObject().Select(property => property.Name).ToList();
    }

    #endregion
}
