using System.Text.Json;
using System.Text.Json.Nodes;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests.Documents;

[TestFixture]
public sealed class SmtLiveSchemaTests
{
    #region Fields

    private string m_folder = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_folder = SmtLiveFixtures.NewFolder();
    }

    [TearDown]
    public void TearDown()
    {
        SmtLiveFixtures.Delete(m_folder);
    }

    #endregion

    #region Schema Tests

    [Test]
    public void EverySchemaOfTheControllerIsBesideTheToolAndLoadsTest()
    {
        foreach (var document in SmtLiveDocuments.RUN.Append(SmtLiveDocuments.CAPABILITIES))
            Assert.That(() => SmtLiveDocuments.SchemaOf(document), Throws.Nothing, document.Schema);
    }

    [Test]
    public void TheFixturesHoldToTheControllersSchemasTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var document in SmtLiveDocuments.RUN)
            {
                var verdict = SmtLiveDocumentCheck.CheckFile(SmtLiveFixtures.Run(document), SmtLiveDocuments.SchemaOf(document), document.Lines);
                Assert.That(verdict.Errors, Is.Empty, document.File);
                Assert.That(verdict.IsValid, Is.True, document.File);
            }

            var capabilities = SmtLiveDocumentCheck.CheckFile(SmtLiveFixtures.Capabilities(), SmtLiveDocuments.SchemaOf(SmtLiveDocuments.CAPABILITIES), false);
            Assert.That(capabilities.Errors, Is.Empty);
        });

        var results = SmtLiveDocumentCheck.CheckFile(SmtLiveFixtures.Run(SmtLiveDocuments.RESULTS), SmtLiveDocuments.SchemaOf(SmtLiveDocuments.RESULTS), true);
        Assert.That(results.Items, Is.EqualTo(4));
    }

    [Test]
    public void ASchemaKeywordTheToolDoesNotCheckIsRefusedWhenTheSchemaLoadsTest()
    {
        Assert.That(() => SmtLiveSchema.Parse("s.json", """{ "type": "object", "minProperties": 1 }"""),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("minProperties"));
        Assert.That(() => SmtLiveSchema.Parse("s.json", """{ "properties": { "a": { "type": "decimal" } } }"""),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("decimal"));
    }

    #endregion

    #region Fault Tests

    [Test]
    public void AMissingFieldIsSaidWithItsPathTest()
    {
        var errors = Check(SmtLiveDocuments.INTEGRITY, root => root["summary"]!.AsObject().Remove("lies"));

        Assert.That(errors, Has.Some.EqualTo("$.summary: lacks 'lies'"));
    }

    [Test]
    public void AFieldThatIsNoFieldOfTheObjectIsSaidTest()
    {
        var errors = Check(SmtLiveDocuments.REPORT, root => root["run"]!.AsObject()["machine"] = "a name");

        Assert.That(errors, Has.Some.EqualTo("$.run: has 'machine', which is no field of it"));
    }

    [Test]
    public void AWordOutsideItsEnumAndAValueOfAnotherTypeAreSaidTest()
    {
        var errors = Check(SmtLiveDocuments.REPORT, root =>
        {
            root["run"]!.AsObject()["sampling"] = "maybe";
            root["verdicts"]!.AsObject()["sat"] = "two";
        });

        Assert.That(errors, Has.Some.StartsWith("$.run.sampling: is \"maybe\", none of"));
        Assert.That(errors, Has.Some.EqualTo("$.verdicts.sat: is a string, not \"integer\""));
    }

    [TestCase("abc")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void APatternHoldsForTheWholeTextTest(string digest)
    {
        var errors = CheckRow(0, row => row["digest"] = digest);

        Assert.That(errors, Has.Some.StartsWith("$.digest: is ").And.Some.Contains("does not match ^[0-9a-f]{64}$"));
    }

    [Test]
    public void TheConditionsOfAQueryAreHeldTest()
    {
        var withoutModel = CheckRow(0, row => row.Remove("model"));
        var agreementWithoutFailure = CheckRow(0, row => row["settled_by"] = "agreement");
        var noResultWithAVerdict = CheckRow(1, row => row["status"] = "no_result");

        Assert.That(withoutModel, Has.Some.EqualTo("$: lacks 'model'"), "a check row has its model");
        Assert.That(agreementWithoutFailure, Has.Some.StartsWith("$.validation: is \"validated\""), "agreement only with a failed model");
        Assert.That(noResultWithAVerdict, Has.Some.StartsWith("$.verdict: is \"unsat\""), "no_result has no verdict");
    }

    [Test]
    public void AWholeNumberIsAnIntegerHoweverItIsWrittenTest()
    {
        var schema = SmtLiveSchema.Parse("s.json", """{ "type": "integer", "minimum": 0, "maximum": 10 }""");

        Assert.Multiple(() =>
        {
            Assert.That(schema.Check(Json("2"), 5), Is.Empty);
            Assert.That(schema.Check(Json("2.0"), 5), Is.Empty);
            Assert.That(schema.Check(Json("1e1"), 5), Is.Empty);
            Assert.That(schema.Check(Json("2.5"), 5), Is.Not.Empty);
            Assert.That(schema.Check(Json("11"), 5), Has.Some.Contains("above 10"));
            Assert.That(schema.Check(Json("-1"), 5), Has.Some.Contains("below 0"));
        });
    }

    #endregion

    #region File Tests

    [Test]
    public void TheFaultsOfJsonLinesAreSaidByLineTest()
    {
        var path = Path.Combine(m_folder, SmtLiveDocuments.RESULTS.File);
        var lines = File.ReadAllLines(SmtLiveFixtures.Run(SmtLiveDocuments.RESULTS)).ToList();
        lines[2] = lines[2].Replace("\"verdict\":\"sat\"", "\"verdict\":\"yes\"", StringComparison.Ordinal);
        lines.Insert(3, "not json");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");

        var verdict = SmtLiveDocumentCheck.CheckFile(path, SmtLiveDocuments.SchemaOf(SmtLiveDocuments.RESULTS), true);

        Assert.Multiple(() =>
        {
            Assert.That(verdict.IsValid, Is.False);
            Assert.That(verdict.Items, Is.EqualTo(5));
            Assert.That(verdict.Faulty, Is.EqualTo(2));
            Assert.That(verdict.Errors, Has.Some.StartsWith("line 3: $.verdict: is \"yes\""));
            Assert.That(verdict.Errors, Has.Some.EqualTo("line 4: not one JSON value"));
        });
    }

    [Test]
    public void ADocumentThatIsNotJsonOrNotUtf8IsSaidTest()
    {
        var text = Path.Combine(m_folder, "report.json");
        var bytes = Path.Combine(m_folder, "bytes.json");
        File.WriteAllText(text, "{ \"schema\": ");
        File.WriteAllBytes(bytes, [0x7B, 0xFF, 0x7D]);
        var schema = SmtLiveDocuments.SchemaOf(SmtLiveDocuments.REPORT);

        Assert.That(SmtLiveDocumentCheck.CheckFile(text, schema, false).Errors, Has.Some.StartsWith("not one JSON value"));
        Assert.That(SmtLiveDocumentCheck.CheckFile(bytes, schema, false).Errors, Has.Some.EqualTo("the file is not valid UTF-8"));
    }

    #endregion

    #region Tools

    private static IReadOnlyList<string> Check(SmtLiveDocument document, Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(SmtLiveFixtures.Run(document)))!.AsObject();
        edit(root);
        return SmtLiveDocuments.SchemaOf(document).Check(Json(root.ToJsonString()), 10);
    }

    private static IReadOnlyList<string> CheckRow(int index, Action<JsonObject> edit)
    {
        var line = File.ReadAllLines(SmtLiveFixtures.Run(SmtLiveDocuments.RESULTS))[index];
        var row = JsonNode.Parse(line)!.AsObject();
        edit(row);
        return SmtLiveDocuments.SchemaOf(SmtLiveDocuments.RESULTS).Check(Json(row.ToJsonString()), 10);
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    #endregion
}
