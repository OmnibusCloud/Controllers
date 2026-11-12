using System.Text.Json;

namespace OutWit.Controller.AI.Verify.Tests.Docs;

/// <summary>
/// The published document vocabulary (docs/*.schema.json) stays in step with the parser and the
/// writers: every schema parses, names itself, and lists exactly the fields the code reads or
/// writes. A new field lands in the schema in the same change as in the code.
/// </summary>
[TestFixture]
public sealed class VerifySchemaDocsTests
{
    #region Constants

    private static readonly string[] SCHEMAS =
    [
        "verify-taskset.schema.json",
        "verify-checker.schema.json",
        "verify-report.schema.json",
        "verify-corpus.schema.json",
        "verify-provenance.schema.json",
        "verify-integrity.schema.json"
    ];

    #endregion

    #region Tests

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

    [Test]
    public void TasksetSchemaListsTheParserFieldsTest()
    {
        using var document = Load("verify-taskset.schema.json");
        var properties = PropertyNames(document.RootElement.GetProperty("properties"));
        var caseProperties = PropertyNames(document.RootElement.GetProperty("properties").GetProperty("suite").GetProperty("items").GetProperty("properties"));
        var limitProperties = PropertyNames(document.RootElement.GetProperty("properties").GetProperty("limits").GetProperty("properties"));

        Assert.Multiple(() =>
        {
            Assert.That(properties, Is.EquivalentTo(new[] { "index", "runtime", "entry", "sources", "args", "stdin", "seed", "suite", "limits" }));
            Assert.That(caseProperties, Is.EquivalentTo(new[] { "stdin", "args", "expected_stdout", "expected_exit" }));
            Assert.That(limitProperties, Is.EquivalentTo(new[] { "fuel", "memory", "wall_ms", "stdout_bytes", "stderr_bytes" }));
        });
    }

    [Test]
    public void CheckerSchemaIsATaskLinePlusTheVerdictRuleTest()
    {
        using var document = Load("verify-checker.schema.json");
        var properties = PropertyNames(document.RootElement.GetProperty("properties"));

        Assert.That(properties, Is.EquivalentTo(new[] { "runtime", "entry", "sources", "args", "limits", "expected_stdout", "expected_exit" }));
    }

    [Test]
    public void ReportSchemaListsTheWriterFieldsTest()
    {
        using var document = Load("verify-report.schema.json");
        var root = document.RootElement.GetProperty("properties");
        var summary = PropertyNames(root.GetProperty("summary").GetProperty("properties"));
        var result = PropertyNames(root.GetProperty("results").GetProperty("items").GetProperty("properties"));
        var verdicts = root.GetProperty("results").GetProperty("items").GetProperty("properties")
            .GetProperty("verdict").GetProperty("enum").EnumerateArray().Select(v => v.GetString()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(summary, Is.EquivalentTo(new[] { "total", "pass", "pass_rate", "verdicts" }));
            Assert.That(result, Is.EquivalentTo(new[] { "index", "verdict", "exit", "cases" }));
            Assert.That(verdicts, Is.EquivalentTo(Enum.GetNames<OutWit.Controller.AI.Verify.Model.VerifyVerdict>()));
        });
    }

    [Test]
    public void CorpusAndProvenanceSchemasListTheWriterFieldsTest()
    {
        using var corpus = Load("verify-corpus.schema.json");
        using var provenance = Load("verify-provenance.schema.json");

        var corpusProperties = PropertyNames(corpus.RootElement.GetProperty("properties"));
        var batchItem = provenance.RootElement.GetProperty("properties").GetProperty("batches").GetProperty("items").GetProperty("properties");
        var batchProperties = PropertyNames(batchItem);
        var metricProperties = PropertyNames(batchItem.GetProperty("metrics").GetProperty("items").GetProperty("properties"));

        Assert.Multiple(() =>
        {
            Assert.That(corpusProperties, Is.EquivalentTo(new[] { "index", "verdict", "exit", "stdout", "stdout_truncated", "stderr", "stderr_truncated", "cases" }));
            Assert.That(PropertyNames(corpus.RootElement.GetProperty("properties").GetProperty("cases").GetProperty("items").GetProperty("properties")),
                Is.EquivalentTo(new[] { "case", "passed", "verdict", "exit", "stdout" }));
            Assert.That(batchProperties, Is.EquivalentTo(new[] { "runtime_target", "wasmtime", "controller", "runtime", "runtime_sha256", "degree", "wall_ms", "fuel", "tasks", "metrics" }));
            Assert.That(metricProperties, Is.EquivalentTo(new[] { "index", "fuel", "peak_memory", "wall_ms" }));
        });
    }

    [Test]
    public void CapabilitiesSchemaListsTheWriterFieldsTest()
    {
        using var schema = Load("verify-capabilities.schema.json");
        var properties = schema.RootElement.GetProperty("properties");

        // What the writer produces, with a pool, against what the schema allows.
        var diagnostics = new OutWit.Controller.AI.Verify.Model.VerifyRuntimeDiagnosticsData
        {
            Runtimes = [new OutWit.Controller.AI.Verify.Model.VerifyRuntimeInfoData { RuntimeId = "python-3.14.6" }]
        };
        using var written = JsonDocument.Parse(OutWit.Controller.AI.Verify.Runtimes.VerifyCapabilitiesWriter.Write(
            diagnostics, new OutWit.Controller.AI.Verify.Runtimes.VerifyPoolSummary(1, 1, 1, 1)));
        var root = written.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(PropertyNames(root), Is.EquivalentTo(PropertyNames(properties)));
            Assert.That(PropertyNames(root.GetProperty("runtimes")[0]), Is.EquivalentTo(PropertyNames(properties.GetProperty("runtimes").GetProperty("items").GetProperty("properties"))));
            Assert.That(PropertyNames(root.GetProperty("defaults")), Is.EquivalentTo(PropertyNames(properties.GetProperty("defaults").GetProperty("properties"))));
            Assert.That(PropertyNames(root.GetProperty("ceilings")), Is.EquivalentTo(PropertyNames(properties.GetProperty("ceilings").GetProperty("properties"))));
            Assert.That(PropertyNames(root.GetProperty("packages")), Is.EquivalentTo(PropertyNames(properties.GetProperty("packages").GetProperty("properties"))));
            Assert.That(PropertyNames(root.GetProperty("pool")), Is.EquivalentTo(PropertyNames(properties.GetProperty("pool").GetProperty("properties"))));
            Assert.That(properties.GetProperty("schema").GetProperty("const").GetString(), Is.EqualTo(root.GetProperty("schema").GetString()));
        });
    }

    [Test]
    public void IntegritySchemaListsTheWriterFieldsTest()
    {
        using var document = Load("verify-integrity.schema.json");
        var root = document.RootElement.GetProperty("properties");
        var summary = PropertyNames(root.GetProperty("summary").GetProperty("properties"));
        var pair = PropertyNames(root.GetProperty("pairs").GetProperty("items").GetProperty("properties"));
        var side = PropertyNames(document.RootElement.GetProperty("$defs").GetProperty("side").GetProperty("properties"));

        Assert.Multiple(() =>
        {
            Assert.That(summary, Is.EquivalentTo(new[] { "sampled", "compared", "mismatches", "mismatch_rate" }));
            Assert.That(pair, Is.EquivalentTo(new[] { "index", "primary", "replica", "compared", "match", "differs" }));
            Assert.That(side, Is.EquivalentTo(new[] { "verdict", "exit" }));
        });
    }

    #endregion

    #region Tools

    private static JsonDocument Load(string fileName)
    {
        var root = FindSolutionRoot();
        if (root == null)
            Assert.Ignore("Solution root not found");

        var path = Path.Combine(root, "AI", "OutWit.Controller.AI.Verify", "docs", fileName);
        Assert.That(File.Exists(path), Is.True, $"schema missing: {path}");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }

    private static List<string> PropertyNames(JsonElement properties)
    {
        return properties.EnumerateObject().Select(property => property.Name).ToList();
    }

    private static string? FindSolutionRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "OutWit.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    #endregion
}
