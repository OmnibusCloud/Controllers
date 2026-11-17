using System.Globalization;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Docs;

/// <summary>
/// The parser and the published schemas, held to each other from the schema files themselves
/// (audit finding V:M-4, hardening item B2): every field a schema lists is taken, a field it
/// does not list is refused, every integer is held to the range the schema states, and the
/// counts a schema bounds are the code's constants. The schema is the contract a lab writes
/// to; what the parser accepts is exactly that.
/// </summary>
[TestFixture]
public sealed class VerifySchemaAgreementTests
{
    #region Constants

    private const string TASKSET_SCHEMA = "verify-taskset.schema.json";
    private const string CHECKER_SCHEMA = "verify-checker.schema.json";
    private const string TASK = """{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}""";

    #endregion

    #region Tests

    [Test]
    public void M4TheParserTakesEveryFieldTheTasksetSchemaListsAndNoOtherTest()
    {
        using var schema = Load(TASKSET_SCHEMA);
        var root = schema.RootElement.GetProperty("properties");
        var suiteCase = root.GetProperty("suite").GetProperty("items").GetProperty("properties");
        var limits = root.GetProperty("limits").GetProperty("properties");

        Assert.Multiple(() =>
        {
            Assert.That(schema.RootElement.GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("suite").GetProperty("items").GetProperty("additionalProperties").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("limits").GetProperty("additionalProperties").GetBoolean(), Is.False);

            foreach (var field in root.EnumerateObject().Where(field => field.Name is not ("runtime" or "sources")))
            {
                Assert.That(Errors(TASK + $",\"{field.Name}\":{Sample(field)}}}"), Is.Empty, $"the schema lists '{field.Name}'");
                Assert.That(Errors(TASK + $",\"{field.Name}x\":{Sample(field)}}}"), Is.EqualTo(new[] { $"line 1: unknown field '{field.Name}x'" }));
            }

            foreach (var field in suiteCase.EnumerateObject())
            {
                Assert.That(Errors(TASK + $",\"suite\":[{{\"{field.Name}\":{Sample(field)}}}]}}"), Is.Empty, $"the schema lists 'suite[].{field.Name}'");
                Assert.That(Errors(TASK + $",\"suite\":[{{\"{field.Name}x\":{Sample(field)}}}]}}"), Is.EqualTo(new[] { $"line 1: unknown field 'suite[0].{field.Name}x'" }));
            }

            foreach (var field in limits.EnumerateObject())
            {
                Assert.That(Errors(TASK + $",\"limits\":{{\"{field.Name}\":{Sample(field)}}}}}"), Is.Empty, $"the schema lists 'limits.{field.Name}'");
                Assert.That(Errors(TASK + $",\"limits\":{{\"{field.Name}x\":{Sample(field)}}}}}"), Is.EqualTo(new[] { $"line 1: unknown field 'limits.{field.Name}x'" }));
            }

            // What a schema requires, the parser requires.
            Assert.That(schema.RootElement.GetProperty("required").EnumerateArray().Select(name => name.GetString()), Is.EquivalentTo(new[] { "runtime", "sources" }));
            Assert.That(Errors("""{"sources":{"m.py":"print(1)"}}"""), Is.EqualTo(new[] { "line 1: missing 'runtime'" }));
            Assert.That(Errors("""{"runtime":"python-3.14.6"}"""), Is.EqualTo(new[] { "line 1: missing or empty 'sources'" }));
            Assert.That(Errors("""{"runtime":"python-3.14.6","sources":{}}"""), Is.EqualTo(new[] { "line 1: missing or empty 'sources'" }), "minProperties: 1");
        });
    }

    [Test]
    public void M4TheParserHoldsEveryIntegerToTheRangeTheSchemaStatesTest()
    {
        using var schema = Load(TASKSET_SCHEMA);
        var root = schema.RootElement.GetProperty("properties");

        var integers = new List<(string Name, string Before, string After, JsonElement Schema)>();
        foreach (var field in root.EnumerateObject().Where(IsInteger))
            integers.Add((field.Name, TASK + $",\"{field.Name}\":", "}", field.Value));
        foreach (var field in root.GetProperty("suite").GetProperty("items").GetProperty("properties").EnumerateObject().Where(IsInteger))
            integers.Add(($"suite[0].{field.Name}", TASK + $",\"suite\":[{{\"{field.Name}\":", "}]}", field.Value));
        foreach (var field in root.GetProperty("limits").GetProperty("properties").EnumerateObject().Where(IsInteger))
            integers.Add(($"limits.{field.Name}", TASK + $",\"limits\":{{\"{field.Name}\":", "}}", field.Value));

        Assert.Multiple(() =>
        {
            Assert.That(integers.Select(field => field.Name), Is.EquivalentTo(new[]
            {
                "index", "seed", "suite[0].expected_exit", "limits.fuel", "limits.memory", "limits.wall_ms", "limits.stdout_bytes", "limits.stderr_bytes"
            }), "every integer of the schema is held here");

            foreach (var (name, before, after, field) in integers)
            {
                Assert.That(field.TryGetProperty("minimum", out var least) && field.TryGetProperty("maximum", out var most), Is.True, $"the schema states the range of '{name}'");
                var minimum = decimal.Parse(least.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
                var maximum = decimal.Parse(field.GetProperty("maximum").GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);

                Assert.That(Errors(before + Number(minimum) + after), Is.Empty, $"'{name}' at its minimum");
                Assert.That(Errors(before + Number(maximum) + after), Is.Empty, $"'{name}' at its maximum");
                Assert.That(Errors(before + Number(minimum - 1) + after), Is.EqualTo(new[] { $"line 1: '{name}' must be {Number(minimum)} or more" }));
                Assert.That(Errors(before + Number(maximum + 1) + after), Is.EqualTo(new[] { $"line 1: '{name}' must be {Number(maximum)} or less" }));
                Assert.That(Errors(before + "1.5" + after), Is.EqualTo(new[] { $"line 1: '{name}' must be an integer" }));
            }
        });
    }

    [Test]
    public void M4TheCountsTheSchemasBoundAreTheCodesTest()
    {
        using var taskset = Load(TASKSET_SCHEMA);
        using var checker = Load(CHECKER_SCHEMA);
        var root = taskset.RootElement.GetProperty("properties");
        var check = checker.RootElement.GetProperty("properties");

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("sources").GetProperty("maxProperties").GetInt32(), Is.EqualTo(VerifyTasksetLimits.MAX_SOURCES));
            Assert.That(root.GetProperty("args").GetProperty("maxItems").GetInt32(), Is.EqualTo(VerifyTasksetLimits.MAX_ARGS));
            Assert.That(root.GetProperty("suite").GetProperty("maxItems").GetInt32(), Is.EqualTo(VerifyTasksetLimits.MAX_SUITE_CASES));
            Assert.That(root.GetProperty("suite").GetProperty("items").GetProperty("properties").GetProperty("args").GetProperty("maxItems").GetInt32(), Is.EqualTo(VerifyTasksetLimits.MAX_ARGS));
            Assert.That(check.GetProperty("sources").GetProperty("maxProperties").GetInt32(), Is.EqualTo(VerifyTasksetLimits.MAX_SOURCES));
            Assert.That(check.GetProperty("args").GetProperty("maxItems").GetInt32(), Is.EqualTo(VerifyTasksetLimits.MAX_ARGS));

            // The field that is accepted and ignored says so in the schema's own word.
            Assert.That(root.GetProperty("limits").GetProperty("properties").GetProperty("wall_ms").GetProperty("deprecated").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("limits").GetProperty("properties").EnumerateObject().Count(field => field.Value.TryGetProperty("deprecated", out _)), Is.EqualTo(1));
        });
    }

    [Test]
    public void M4TheParserTakesEveryFieldTheCheckerSchemaListsAndNoOtherTest()
    {
        using var schema = Load(CHECKER_SCHEMA);
        var root = schema.RootElement.GetProperty("properties");

        Assert.Multiple(() =>
        {
            Assert.That(schema.RootElement.GetProperty("additionalProperties").GetBoolean(), Is.False);

            foreach (var field in root.EnumerateObject().Where(field => field.Name is not ("runtime" or "sources")))
            {
                var sample = field.Name == "limits" ? "{}" : Sample(field);
                Assert.DoesNotThrow(() => VerifyCheckerSpec.Parse(TASK + $",\"{field.Name}\":{sample}}}"), $"the schema lists '{field.Name}'");

                var refused = Assert.Throws<FormatException>(() => VerifyCheckerSpec.Parse(TASK + $",\"{field.Name}x\":{sample}}}"));
                Assert.That(refused?.Message, Does.EndWith($"unknown field '{field.Name}x'"));
            }

            // A task's fields that a checker does not have.
            foreach (var field in new[] { "index", "stdin", "seed", "suite" })
            {
                Assert.That(root.TryGetProperty(field, out _), Is.False);
                Assert.Throws<FormatException>(() => VerifyCheckerSpec.Parse(TASK + $",\"{field}\":0}}"), field);
            }
        });
    }

    #endregion

    #region Tools

    private static List<string> Errors(string line)
    {
        return VerifyTasksetParser.Parse(line).Errors;
    }

    private static bool IsInteger(JsonProperty field)
    {
        return field.Value.TryGetProperty("type", out var type) && type.GetString() == "integer";
    }

    /// <summary>A value of a field's type, as JSON.</summary>
    private static string Sample(JsonProperty field)
    {
        if (field.Name == "entry")
            return "\"m.py\"";

        return field.Value.GetProperty("type").GetString() switch
        {
            "integer" => "1",
            "string" => "\"x\"",
            "array" => "[]",
            "object" => "{}",
            var other => throw new InvalidOperationException($"a field of type '{other}'")
        };
    }

    private static string Number(decimal value)
    {
        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    private static JsonDocument Load(string fileName)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.slnx")))
            directory = directory.Parent;
        if (directory == null)
            Mock.VerifyRigModules.Missing("Solution root not found");

        var path = Path.Combine(directory!.FullName, "AI", "OutWit.Controller.AI.Verify", "docs", fileName);
        Assert.That(File.Exists(path), Is.True, $"schema missing: {path}");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }

    #endregion
}
