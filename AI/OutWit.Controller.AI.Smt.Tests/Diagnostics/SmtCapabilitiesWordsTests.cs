using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Controller.AI.Smt.Tests.Docs;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;

namespace OutWit.Controller.AI.Smt.Tests.Diagnostics;

/// <summary>
/// The words of the capabilities document: a lab builds its legends and its filters from them, so
/// every word a document of a run may say is listed there, and no word it never says. Each list is
/// held to the schemas of the documents: every enum of the results, the report, the integrity
/// document and the provenance, and every table of the report and the integrity document whose
/// keys are words, is a list of the capabilities. A word added to the code and to a schema and
/// missed in the capabilities, or added to the code and missed in a schema, fails here.
/// </summary>
[TestFixture]
public sealed class SmtCapabilitiesWordsTests
{
    #region Constants

    private const string RESULTS = "smt-results.schema.json";
    private const string REPORT = "smt-report.schema.json";
    private const string INTEGRITY = "smt-integrity.schema.json";
    private const string PROVENANCE = "smt-provenance.schema.json";

    /// <summary>Every enum of a document of a run (outside its conditions), by schema and place, and the list of the capabilities that holds its words.</summary>
    private static readonly IReadOnlyDictionary<(string Schema, string Place), string> ENUMS = new Dictionary<(string, string), string>
    {
        [(RESULTS, "/query")] = "/queries",
        [(RESULTS, "/status")] = "/results/statuses",
        [(RESULTS, "/verdict")] = "/results/verdicts",
        [(RESULTS, "/reason")] = "/results/reasons",
        [(RESULTS, "/expected")] = "/results/expected",
        [(RESULTS, "/expected_agreement")] = "/results/expected_agreements",
        [(RESULTS, "/proof")] = "/results/proofs",
        [(RESULTS, "/validation")] = "/results/validations",
        [(RESULTS, "/settled_by")] = "/results/settled_by",
        [(REPORT, "/run/sampling")] = "/run/sampling",
        [(REPORT, "/run/binding")] = "/run/bindings",
        [(REPORT, "/run/options/query")] = "/queries",
        [(REPORT, "/long_tail/problems/[]/stopped_by")] = "/run/stopped_by",
        [(INTEGRITY, "/summary/sampling")] = "/run/sampling",
        [(INTEGRITY, "/summary/binding")] = "/run/bindings",
        [(INTEGRITY, "/fan_outs/[]/round")] = "/run/rounds",
        [(INTEGRITY, "/disputes/[]/cause")] = "/run/dispute_causes",
        [(INTEGRITY, "/disputes/[]/outcome")] = "/run/dispute_outcomes",
        [(PROVENANCE, "/options/query")] = "/queries"
    };

    /// <summary>
    /// Every table of the report and the integrity document whose keys are words: the object's
    /// place, the list of the capabilities its keys are, and the words of the list a table leaves
    /// out (a table of answers has no key for "no verdict").
    /// </summary>
    private static readonly IReadOnlyList<(string Schema, string Place, string List, string[] LeftOut)> TABLES =
    [
        (REPORT, "/statuses", "/results/statuses", []),
        (REPORT, "/verdicts", "/results/verdicts", [SmtResultWords.NONE]),
        (REPORT, "/unknown", "/results/reasons", [SmtResultWords.NONE]),
        (REPORT, "/expected", "/results/expected_agreements", []),
        (REPORT, "/validation/classes", "/results/validations", [SmtRunWords.NOT_CHECKED]),
        (REPORT, "/copies/disputes", "/run/dispute_outcomes", []),
        (REPORT, "/copies/settled_by", "/results/settled_by", []),
        (INTEGRITY, "/summary/dispute_causes", "/run/dispute_causes", []),
        (INTEGRITY, "/summary/dispute_outcomes", "/run/dispute_outcomes", []),
        (INTEGRITY, "/summary/rejected", "/run/rejected", []),
        (INTEGRITY, "/summary/node_faults_by_code", "/results/node_faults", [])
    ];

    #endregion

    #region Status Tests

    [Test]
    public void TheStatusesAreTheWordsOfAResultsRowTest()
    {
        var statuses = Words(Capabilities(), "/results/statuses");

        Assert.Multiple(() =>
        {
            Assert.That(statuses, Is.EquivalentTo(Enum(RESULTS, "/status")), "a row of the results says these statuses and no other");
            Assert.That(statuses, Does.Not.Contain(SmtResultWords.NODE_FAULT), "a node's fault is never a row of the results");
            Assert.That(statuses, Does.Contain(SmtRunWords.NO_RESULT).And.Contain(SmtRunWords.DISPUTED), "the server's own statuses are");
        });
    }

    #endregion

    #region Code Tests

    [Test]
    public void EveryListIsTheCodesOwnTest()
    {
        var capabilities = Capabilities();

        Assert.Multiple(() =>
        {
            Assert.That(Words(capabilities, "/queries"), Is.EqualTo(new[] { "check", "enumerate", "prove" }));
            Assert.That(Words(capabilities, "/results/statuses"), Is.EqualTo(SmtRunWords.STATUSES));
            Assert.That(Words(capabilities, "/results/codes/refused"), Is.EquivalentTo(SmtRefusalCodes.All));
            Assert.That(Words(capabilities, "/results/codes/refused"), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal), "in an order of their own, whatever the set's");
            Assert.That(Words(capabilities, "/results/codes/engine_crash"), Is.EqualTo(new[] { "trap", "exit", "engine_error", "unreadable_answer" }));
            Assert.That(Words(capabilities, "/results/codes/no_result"), Is.EqualTo(SmtRunSettlement.NO_RESULT_REASONS));
            Assert.That(Words(capabilities, "/results/codes/disputed"), Is.EqualTo(SmtRunWords.DISPUTE_OUTCOMES));
            Assert.That(Words(capabilities, "/results/verdicts"), Is.EqualTo(SmtResultWords.VERDICTS));
            Assert.That(Words(capabilities, "/results/reasons"), Is.EqualTo(SmtResultWords.REASONS));
            Assert.That(Words(capabilities, "/results/proofs"), Is.EqualTo(SmtResultWords.PROOFS));
            Assert.That(Words(capabilities, "/results/expected"), Is.EqualTo(new[] { "sat", "unsat", "none" }));
            Assert.That(Words(capabilities, "/results/expected_agreements"), Is.EqualTo(SmtRunWords.EXPECTATIONS));
            Assert.That(Words(capabilities, "/results/validations"), Is.EqualTo(SmtRunWords.VALIDATIONS));
            Assert.That(Words(capabilities, "/results/validation_reasons"), Is.EqualTo(SmtValidationReasons.All.Append(SmtRunWords.OUTPUT_EXCEEDED)));
            Assert.That(Words(capabilities, "/results/settled_by"), Is.EqualTo(SmtRunWords.SETTLED_BY));
            Assert.That(Words(capabilities, "/results/node_faults"), Is.EqualTo(SmtNodeFaults.ALL));
            Assert.That(Words(capabilities, "/run/sampling"), Is.EqualTo(new[] { "not_stated", "none", "as_asked", "short" }));
            Assert.That(Words(capabilities, "/run/bindings"), Is.EqualTo(SmtRunWords.BINDINGS));
            Assert.That(Words(capabilities, "/run/rounds"), Is.EqualTo(new[] { "main", "repair", "dispute" }));
            Assert.That(Words(capabilities, "/run/repair_reasons"), Is.EqualTo(SmtRunSettlement.NO_RESULT_REASONS.Append("copies_short")));
            Assert.That(Words(capabilities, "/run/rejected"), Is.EqualTo(new[] { "unknown_ticket", "duplicate", "unreadable", "content", "surplus", "other_package" }));
            Assert.That(Words(capabilities, "/run/dispute_causes"), Is.EqualTo(SmtRunWords.DISPUTE_CAUSES));
            Assert.That(Words(capabilities, "/run/dispute_outcomes"), Is.EqualTo(SmtRunWords.DISPUTE_OUTCOMES));
            Assert.That(Words(capabilities, "/run/stopped_by"), Is.EqualTo(new[] { "rlimit", "fuel", "memout" }));
        });
    }

    [Test]
    public void TheWordsOfANoResultAreThoseTheSettlementGivesTest()
    {
        // What the nodes said last of a problem without a result: a node's refusal of it, the kind
        // of a fault of the node its row names, a node's fault code of its package, the reason its
        // package's result was refused, or no answer at all.
        var reasons = SmtRunSettlement.NO_RESULT_REASONS;

        Assert.Multiple(() =>
        {
            Assert.That(reasons, Is.Unique);
            Assert.That(reasons, Does.Contain(SmtRunWords.NODE_REFUSED).And.Contain(SmtRunWords.NO_ANSWER));
            Assert.That(reasons, Is.SupersetOf(SmtNodeFaults.ALL));
            Assert.That(reasons, Is.SupersetOf(new[] { "sandbox_unavailable", "backstop", "cancelled", "not_instantiated", "host_fault" }));
            Assert.That(reasons, Is.SupersetOf(new[] { "unknown_ticket", "duplicate", "unreadable", "content", "surplus", "other_package" }));
            Assert.That(reasons, Does.Not.Contain(SmtResultWords.NODE_FAULT), "a row of a node's fault always names its kind");
        });
    }

    [Test]
    public void TheDocumentHoldsToItsSchemaTest()
    {
        var capabilities = Capabilities();

        Assert.Multiple(() =>
        {
            Assert.That(SmtSchemaCheck.IsValid(SmtRunChecks.Schema("smt-capabilities.schema.json"), capabilities), Is.True);
            Assert.That(capabilities.GetRawText(), Does.Not.Contain("\"\""), "an empty word is the documents' own for no word, never one of a list");
        });
    }

    #endregion

    #region Schema Tests

    [Test]
    public void EveryEnumOfTheDocumentsIsAListOfTheCapabilitiesTest()
    {
        var capabilities = Capabilities();

        Assert.Multiple(() =>
        {
            foreach (var schema in new[] { RESULTS, REPORT, INTEGRITY, PROVENANCE })
            {
                foreach (var (place, words) in Enums(SmtRunChecks.Schema(schema), ""))
                {
                    if (!ENUMS.TryGetValue((schema, place), out var list))
                    {
                        Assert.Fail($"{schema} {place}: an enum of a document that the capabilities do not list");
                        continue;
                    }

                    Assert.That(Words(capabilities, list), Is.EquivalentTo(words.Where(word => word.Length > 0)), $"{schema} {place} against the capabilities' {list}");
                }
            }
        });
    }

    [Test]
    public void EveryTableOfWordsIsAListOfTheCapabilitiesTest()
    {
        var capabilities = Capabilities();

        Assert.Multiple(() =>
        {
            foreach (var (schema, place, list, leftOut) in TABLES)
            {
                var keys = At(SmtRunChecks.Schema(schema), place).GetProperty("required").EnumerateArray().Select(key => key.GetString()!);
                Assert.That(Words(capabilities, list).Except(leftOut), Is.EquivalentTo(keys), $"{schema} {place} against the capabilities' {list}");
            }
        });
    }

    #endregion

    #region Tools

    /// <summary>The capabilities document of this process's controller, without a pool.</summary>
    private static JsonElement Capabilities()
    {
        using var document = JsonDocument.Parse(SmtDiagnosticsRig.Capabilities(new SmtRuntimeDiagnosticsData { ModulePresent = true }));
        return document.RootElement.Clone();
    }

    /// <summary>A list of words of the capabilities, by its place ("/results/statuses"); empty when the document has none there.</summary>
    private static List<string> Words(JsonElement capabilities, string place)
    {
        var current = capabilities;
        foreach (var name in place.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                return [];
        }

        return current.ValueKind == JsonValueKind.Array ? current.EnumerateArray().Select(word => word.GetString()!).ToList() : [];
    }

    /// <summary>The words of one enum of a schema, by its place.</summary>
    private static List<string> Enum(string schema, string place)
    {
        return Enums(SmtRunChecks.Schema(schema), "").Single(found => found.Place == place).Words;
    }

    /// <summary>
    /// Every enum of a schema, by the place of its value in a document: the names of the
    /// properties, "[]" for the items of an array. The conditions (allOf) only narrow what the
    /// properties allow and are not walked.
    /// </summary>
    private static IEnumerable<(string Place, List<string> Words)> Enums(JsonElement schema, string place)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            yield break;

        if (schema.TryGetProperty("enum", out var words))
            yield return (place, words.EnumerateArray().Select(word => word.GetString()!).ToList());

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                foreach (var found in Enums(property.Value, $"{place}/{property.Name}"))
                    yield return found;
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            foreach (var found in Enums(items, $"{place}/[]"))
                yield return found;
        }
    }

    /// <summary>The schema of a value of a document, by the place of the value.</summary>
    private static JsonElement At(JsonElement schema, string place)
    {
        var current = schema;
        foreach (var name in place.Split('/', StringSplitOptions.RemoveEmptyEntries))
            current = name == "[]" ? current.GetProperty("items") : current.GetProperty("properties").GetProperty(name);

        return current;
    }

    #endregion
}
