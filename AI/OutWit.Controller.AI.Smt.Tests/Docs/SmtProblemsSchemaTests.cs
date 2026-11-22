using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Tests.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Docs;

/// <summary>
/// The reader of a problem set and the published schema of a row, held to each other from the
/// schema file itself: every field the schema lists is taken and a name beside it is refused,
/// the numbers the schema states are the code's constants, and over a table of rows the reader
/// takes a row exactly when the schema allows it. The schema is the contract a lab writes to;
/// what the reader takes is exactly that, wherever a schema can state the rule.
/// </summary>
[TestFixture]
public class SmtProblemsSchemaTests
{
    #region Constants

    private const string SCHEMA = "smt-problems.schema.json";

    /// <summary>A problem that declares every word a field may hold as a constant, so that a word in a field's place is a name the problem knows.</summary>
    private const string PROBLEM = "(declare-const x Int)(declare-const y Int)(declare-const check Bool)(declare-const enumerate Bool)(declare-const prove Bool)(declare-const sat Bool)(declare-const unsat Bool)(assert (> x y))";

    private static readonly string[] FIELDS = ["id", "smt2", "query", "values", "project", "max_models", "goal", "expected", "tags"];
    private static readonly string[] QUERIES = ["check", "enumerate", "prove"];

    /// <summary>What a row of each query cannot do without, as fields.</summary>
    private static readonly Dictionary<string, (string Name, string Value)[]> NEEDED = new(StringComparer.Ordinal)
    {
        ["check"] = [],
        ["enumerate"] = [("project", "[\"x\"]")],
        ["prove"] = [("goal", "\"x\"")]
    };

    /// <summary>A value every field takes, and a query whose rows have the field.</summary>
    private static readonly Dictionary<string, (string Value, string Query)> SAMPLES = new(StringComparer.Ordinal)
    {
        ["id"] = ("\"a\"", "check"), ["smt2"] = ($"\"{PROBLEM}\"", "check"), ["query"] = ("\"check\"", "check"), ["values"] = ("[\"x\"]", "check"),
        ["project"] = ("[\"y\"]", "enumerate"), ["max_models"] = ("7", "enumerate"), ["goal"] = ("\"y\"", "prove"), ["expected"] = ("\"sat\"", "check"),
        ["tags"] = ("\"t\"", "check")
    };

    /// <summary>Values of every JSON type. Every string among them is a term, and a name the problem declares.</summary>
    private static readonly string[] VALUES =
    [
        "null", "true", "false", "0", "1", "2", "1000", "1001", "-1", "1.5", "2.0", "1e2", "0.5e1", "1e400", "{}", "{\"a\":1}",
        "[]", "[\"x\"]", "[\"x\",\"y\"]", "[\"x\",\"x\"]", "[1]", "[\"x\",null]", "[[\"x\"]]", "[\"check\",\"sat\"]",
        "\"x\"", "\"y\"", "\"check\"", "\"enumerate\"", "\"prove\"", "\"sat\"", "\"unsat\""
    ];

    #endregion

    #region Schema Tests

    [Test]
    public void SchemaParsesAndNamesItselfTest()
    {
        using var schema = Load();
        var root = schema.RootElement;

        Assert.That(root.GetProperty("$schema").GetString(), Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
        Assert.That(root.GetProperty("$id").GetString(), Does.EndWith("/AI/OutWit.Controller.AI.Smt.Model/docs/" + SCHEMA));
        Assert.That(root.GetProperty("title").GetString(), Is.Not.Empty);
        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("object"));
        Assert.That(root.GetProperty("additionalProperties").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("required").EnumerateArray().Select(name => name.GetString()), Is.EquivalentTo(new[] { "id", "smt2" }));
        Assert.That(root.GetProperty("properties").EnumerateObject().Select(field => field.Name), Is.EquivalentTo(FIELDS));

        foreach (var field in root.GetProperty("properties").EnumerateObject())
            Assert.That(field.Value.GetProperty("description").GetString(), Is.Not.Empty, $"'{field.Name}' is described");
    }

    [Test]
    public void ReaderTakesEveryFieldTheSchemaListsAndNoNameBesideItTest()
    {
        using var schema = Load();

        foreach (var field in schema.RootElement.GetProperty("properties").EnumerateObject())
        {
            var (value, query) = SAMPLES[field.Name];
            var row = Fields(query).Where(part => part.Name != field.Name).Append((field.Name, value)).ToList();
            var misspelt = row.Append((field.Name + "x", value)).ToList();

            Assert.That(Reads(Line(row)), Is.Null, $"the schema lists '{field.Name}'");
            Assert.That(SmtSchemaCheck.IsValid(schema.RootElement, Parse(Line(row))), Is.True, field.Name);

            var refusal = Reads(Line(misspelt));
            Assert.That(refusal?.Code, Is.EqualTo(SmtRefusalCodes.FIELD_NOT_ALLOWED));
            Assert.That(refusal!.Reason, Is.EqualTo($"'{field.Name}x' is no field of a row"));
            Assert.That(SmtSchemaCheck.IsValid(schema.RootElement, Parse(Line(misspelt))), Is.False, field.Name + "x");
        }
    }

    [Test]
    public void NumbersTheSchemaStatesAreTheCodesTest()
    {
        using var schema = Load();
        var root = schema.RootElement;
        var fields = root.GetProperty("properties");
        var description = root.GetProperty("description").GetString()!;

        Assert.That(fields.GetProperty("id").GetProperty("pattern").GetString(), Does.EndWith($"{{1,{SmtProblemSetLimits.MAX_ID_CHARACTERS}}}$"));
        Assert.That((fields.GetProperty("values").GetProperty("minItems").GetInt32(), fields.GetProperty("values").GetProperty("maxItems").GetInt32()), Is.EqualTo((1, SmtProblemSetLimits.MAX_VALUES)));
        Assert.That((fields.GetProperty("project").GetProperty("minItems").GetInt32(), fields.GetProperty("project").GetProperty("maxItems").GetInt32()), Is.EqualTo((1, SmtProblemSetLimits.MAX_PROJECT)));
        Assert.That((fields.GetProperty("max_models").GetProperty("minimum").GetInt32(), fields.GetProperty("max_models").GetProperty("maximum").GetInt32()), Is.EqualTo((1, SmtOptionsRules.MAX_MODELS)));
        Assert.That(fields.GetProperty("tags").GetProperty("maxLength").GetInt32(), Is.EqualTo(SmtProblemSetLimits.MAX_TAGS_BYTES));
        Assert.That(fields.GetProperty("query").GetProperty("enum").EnumerateArray().Select(word => word.GetString()), Is.EqualTo(Enum.GetNames<SmtQuery>().Select(name => name.ToLowerInvariant())));
        Assert.That(fields.GetProperty("expected").GetProperty("enum").EnumerateArray().Select(word => word.GetString()), Is.EqualTo(new[] { "sat", "unsat" }));

        // What a schema cannot state is said in words, with the code's own numbers.
        Assert.That(description, Does.Contain($"at most {SmtProblemSetLimits.MAX_LINE_BYTES} bytes (16 MiB)"));
        Assert.That(description, Does.Contain($"at most {SmtProblemSetLimits.MAX_ROWS} rows"));
        Assert.That(description, Does.Contain($"at most {SmtProblemSetLimits.MAX_TAGS_BYTES} bytes of UTF-8"));
        Assert.That(description, Does.Contain($"at most {SmtSanitizer.TERM_CHARACTERS} characters"));
        Assert.That(fields.GetProperty("smt2").GetProperty("description").GetString(), Does.Contain($"at most {SmtReaderLimits.PROBLEM_CHARACTERS} characters"));
        Assert.That(fields.GetProperty("max_models").GetProperty("description").GetString(), Does.Contain($"without that {SmtOptionsRules.DEFAULT_MAX_MODELS}:"));
        Assert.That(fields.GetProperty("values").GetProperty("description").GetString(), Does.Contain($"1 to {SmtProblemSetLimits.MAX_VALUES},"));
        Assert.That(fields.GetProperty("project").GetProperty("description").GetString(), Does.Contain($"1 to {SmtProblemSetLimits.MAX_PROJECT}:"));
    }

    [Test]
    public void SchemaIsPackedIntoThePackageTest()
    {
        var model = Path.Combine(Root(), "AI", "OutWit.Controller.AI.Smt.Model");
        var project = File.ReadAllText(Path.Combine(model, "OutWit.Controller.AI.Smt.Model.csproj"));
        var version = System.Text.RegularExpressions.Regex.Match(project, "<Version>([^<]+)</Version>").Groups[1].Value;

        Assert.That(project, Does.Contain($"<None Update=\"docs\\{SCHEMA}\" Pack=\"true\" PackagePath=\"docs\\{SCHEMA}\" />"));

        // A Release build makes the package: there the package itself is opened. (A Debug build
        // makes none, and one left by an earlier build says nothing of this one.)
        var release = Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar;
        if (!TestContext.CurrentContext.TestDirectory.Contains(release, StringComparison.Ordinal))
            return;

        var path = Path.Combine(model, "bin", "Release", $"OutWit.Controller.AI.Smt.Model.{version}.nupkg");
        Assert.That(File.Exists(path), Is.True, "the Release build packs the model");

        using var package = System.IO.Compression.ZipFile.OpenRead(path);
        var packed = package.GetEntry($"docs/{SCHEMA}");

        Assert.That(packed, Is.Not.Null, $"docs/{SCHEMA} is in the package; it holds: {string.Join(", ", package.Entries.Select(entry => entry.FullName).Where(name => name.StartsWith("docs", StringComparison.Ordinal)))}");
        using var stream = packed!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        Assert.That(memory.ToArray(), Is.EqualTo(File.ReadAllBytes(Path.Combine(model, "docs", SCHEMA))), "the schema in the package is the schema of the repository");
    }

    [Test]
    public void RowsTheReadmeShowsAreRowsTheReaderTakesAndTheSchemaAllowsTest()
    {
        // The example a lab copies first: it must be a set the reader takes whole.
        using var schema = Load();
        var readme = File.ReadAllText(Path.Combine(Root(), "AI", "OutWit.Controller.AI.Smt.Model", "README.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = readme.IndexOf("```json\n", StringComparison.Ordinal);
        var end = readme.IndexOf("\n```", start + 1, StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThan(0), "the README shows rows of a problem set");
        var example = readme[(start + "```json\n".Length)..end];

        var (rows, summary) = SmtProblemSets.Read(example);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.Select(row => row.Refusal?.Reason), Is.All.Null);
        Assert.That(summary.Queries.Keys, Is.EquivalentTo(new[] { SmtQuery.Enumerate, SmtQuery.Prove }));

        foreach (var line in example.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Assert.That(SmtSchemaCheck.IsValid(schema.RootElement, Parse(line)), Is.True, line);
    }

    #endregion

    #region Agreement Tests

    [Test]
    public void ReaderTakesARowExactlyWhenTheSchemaAllowsItTest()
    {
        using var schema = Load();
        var rows = Table().Distinct(StringComparer.Ordinal).ToList();
        var valid = 0;

        foreach (var row in rows)
        {
            var allowed = SmtSchemaCheck.IsValid(schema.RootElement, Parse(row));
            var refusal = Reads(row);

            Assert.That(refusal == null, Is.EqualTo(allowed), $"{(allowed ? "the schema allows" : "the schema refuses")} {Shown(row)}; the reader: {refusal?.Code ?? "taken"} {refusal?.Reason}");
            valid += allowed ? 1 : 0;
        }

        TestContext.Out.WriteLine($"{rows.Count} rows: {valid} the schema allows, {rows.Count - valid} it refuses");
        Assert.That(valid, Is.GreaterThan(100));
        Assert.That(rows.Count - valid, Is.GreaterThan(300));
    }

    /// <summary>
    /// The rows of the table: every field of every query with every value, every field left out,
    /// every pair of a query and a field of another, ids of every printable character, and tags
    /// at their bounds. A row that names no query is read as a check here, as the run's options
    /// have it by default, so its fields are those of a check.
    /// </summary>
    private static IEnumerable<string> Table()
    {
        foreach (var query in QUERIES)
        {
            var row = Fields(query);
            yield return Line(row);

            // Every field with every value in its place, and every field left out.
            foreach (var field in FIELDS.Where(field => field != "smt2"))
            {
                // A row without its query is read by the run's query, which a schema cannot see:
                // the query is left out only where the row has no field of one query.
                if (field != "query" || NEEDED[query].Length == 0)
                    yield return Line(row.Where(part => part.Name != field));

                foreach (var value in VALUES.Append(SAMPLES[field].Value))
                    yield return Line(row.Where(part => part.Name != field).Append((field, value)));
            }

            foreach (var value in VALUES.Where(value => !value.StartsWith('"')))
                yield return Line(row.Select(part => part.Name == "smt2" ? (part.Name, value) : part));

            yield return Line(row.Where(part => part.Name != "smt2"));
            yield return Line(row.Append(("extra", "1")));
            yield return Line(row.Append(("Tags", "\"t\"")));

            // The terms of a check at their bound (a term may be asked twice).
            foreach (var count in new[] { 256, 257 })
            {
                var items = string.Join(",", Enumerable.Repeat("\"x\"", count));
                yield return Line(row.Where(part => part.Name != "values").Append(("values", $"[{items}]")));
            }
        }

        // The constants of an enumeration at their bound: so many names, each declared and named once.
        var declared = string.Concat(Enumerable.Range(0, 257).Select(index => $"(declare-const c{index} Bool)"));
        foreach (var count in new[] { 256, 257 })
        {
            var names = string.Join(",", Enumerable.Range(0, count).Select(index => $"\"c{index}\""));
            yield return Line([("id", "\"a\""), ("smt2", $"\"{declared}\""), ("query", "\"enumerate\""), ("project", $"[{names}]")]);
        }

        // A row that names no query: no field of one query, so every reading agrees.
        yield return Line([("id", "\"a\""), ("smt2", $"\"{PROBLEM}\"")]);
        yield return Line([("id", "\"a\""), ("smt2", $"\"{PROBLEM}\""), ("tags", "\"t\""), ("expected", "\"unsat\"")]);
        yield return Line([("id", "\"a\""), ("smt2", $"\"{PROBLEM}\""), ("values", "[\"x\"]")]);
        yield return "{}";

        // Ids: every printable ASCII character, and the lengths around the bound.
        for (var code = 32; code < 127; code++)
        {
            var character = (char)code is '"' or '\\' ? "\\" + (char)code : ((char)code).ToString();
            yield return Line([("id", $"\"i{character}d\""), ("smt2", $"\"{PROBLEM}\"")]);
        }

        foreach (var length in new[] { 0, 1, 63, 64, 65, 200 })
            yield return Line([("id", $"\"{new string('i', length)}\""), ("smt2", $"\"{PROBLEM}\"")]);

        // Tags: the lengths around the bound, and characters a tag may not hold.
        foreach (var length in new[] { 0, 1, 255, 256, 257, 1000 })
            yield return Line([("id", "\"a\""), ("smt2", $"\"{PROBLEM}\""), ("tags", $"\"{new string('t', length)}\"")]);

        foreach (var control in new[] { "\\n", "\\t", "\\r", "\\b", "\\f" })
            yield return Line([("id", "\"a\""), ("smt2", $"\"{PROBLEM}\""), ("tags", $"\"a{control}b\"")]);
    }

    #endregion

    #region Tools

    /// <summary>The fields of a valid row of a query, the query named.</summary>
    private static List<(string Name, string Value)> Fields(string query)
    {
        return new[] { ("id", "\"a\""), ("smt2", $"\"{PROBLEM}\""), ("query", $"\"{query}\"") }.Concat(NEEDED[query]).ToList();
    }

    private static string Line(IEnumerable<(string Name, string Value)> fields)
    {
        return "{" + string.Join(",", fields.Select(field => $"\"{field.Name}\":{field.Value}")) + "}";
    }

    /// <summary>Reads a set of one row with the default options; null when the row is taken.</summary>
    private static SmtRefusalData? Reads(string line)
    {
        return SmtProblemSets.Read(line).Rows.Single().Refusal;
    }

    private static JsonElement Parse(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private static string Shown(string row)
    {
        return row.Length <= 300 ? row.Replace(PROBLEM, "...", StringComparison.Ordinal) : row.Replace(PROBLEM, "...", StringComparison.Ordinal)[..200] + "...";
    }

    private static JsonDocument Load()
    {
        var path = Path.Combine(Root(), "AI", "OutWit.Controller.AI.Smt.Model", "docs", SCHEMA);
        Assert.That(File.Exists(path), Is.True, $"schema missing: {path}");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.slnx")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "the solution root (OutWit.slnx) was not found above the test folder");
        return directory!.FullName;
    }

    #endregion
}
