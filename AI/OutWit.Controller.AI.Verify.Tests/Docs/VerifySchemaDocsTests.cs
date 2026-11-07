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
        "verify-provenance.schema.json"
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
            Assert.That(summary, Is.EquivalentTo(new[] { "total", "pass", "pass_rate", "total_fuel", "verdicts" }));
            Assert.That(result, Is.EquivalentTo(new[] { "index", "verdict", "exit", "fuel", "peak_memory", "cases" }));
            Assert.That(verdicts, Is.EquivalentTo(Enum.GetNames<OutWit.Controller.AI.Verify.Model.VerifyVerdict>()));
        });
    }

    [Test]
    public void CorpusAndProvenanceSchemasListTheWriterFieldsTest()
    {
        using var corpus = Load("verify-corpus.schema.json");
        using var provenance = Load("verify-provenance.schema.json");

        var corpusProperties = PropertyNames(corpus.RootElement.GetProperty("properties"));
        var nodeProperties = PropertyNames(provenance.RootElement.GetProperty("properties").GetProperty("nodes").GetProperty("items").GetProperty("properties"));
        var batchProperties = PropertyNames(provenance.RootElement.GetProperty("properties").GetProperty("batches").GetProperty("items").GetProperty("properties"));

        Assert.Multiple(() =>
        {
            Assert.That(corpusProperties, Is.EquivalentTo(new[] { "index", "verdict", "exit", "stdout", "stdout_truncated", "stderr", "stderr_truncated" }));
            Assert.That(nodeProperties, Is.EquivalentTo(new[] { "node_id", "node_name", "runtime_target", "wasmtime", "native_library", "controller", "batches", "tasks", "runtimes" }));
            Assert.That(batchProperties, Is.EquivalentTo(new[] { "node_id", "node_name", "runtime", "runtime_sha256", "degree", "wall_ms", "tasks" }));
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
