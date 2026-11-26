using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Docs;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>What the tests of a run hold every run's documents to: their schemas, the words the capabilities list, and that they name no machine.</summary>
internal static class SmtRunChecks
{
    #region Constants

    private const string DOCS = "docs";

    #endregion

    #region Functions

    /// <summary>A schema of the controller's <c>docs/</c>.</summary>
    /// <param name="file">The schema's file name.</param>
    /// <returns>The schema.</returns>
    public static JsonElement Schema(string file)
    {
        var path = Path.Combine(SmtRigModules.SolutionRoot()!, "AI", "OutWit.Controller.AI.Smt", DOCS, file);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.Clone();
    }

    /// <summary>Holds every document of a run to its schema, and its words to the lists of the capabilities (<see cref="AssertWords"/>).</summary>
    /// <param name="run">The run.</param>
    public static void AssertSchemas(SmtRunDocuments run)
    {
        var results = Schema("smt-results.schema.json");

        Assert.Multiple(() =>
        {
            foreach (var row in run.Rows)
                Assert.That(SmtSchemaCheck.IsValid(results, row), Is.True, $"a row of the results is not what its schema says: {row.GetRawText()[..Math.Min(400, row.GetRawText().Length)]}");

            Assert.That(SmtSchemaCheck.IsValid(Schema("smt-report.schema.json"), run.Report), Is.True, "the report is what its schema says");
            Assert.That(SmtSchemaCheck.IsValid(Schema("smt-integrity.schema.json"), run.Integrity), Is.True, "the integrity document is what its schema says");
            Assert.That(SmtSchemaCheck.IsValid(Schema("smt-provenance.schema.json"), run.Provenance), Is.True, "the provenance is what its schema says");
            AssertWords(run.Rows, run.Report, run.Integrity);
        });
    }

    /// <summary>Holds every document Smt.Settle wrote of a run made up without nodes to its schema, and its words to the lists of the capabilities (<see cref="AssertWords"/>).</summary>
    /// <param name="run">The documents' texts.</param>
    public static void AssertSchemas(SmtSettledDocuments run)
    {
        var results = Schema("smt-results.schema.json");

        Assert.Multiple(() =>
        {
            foreach (var line in run.Results.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Assert.That(SmtSchemaCheck.IsValid(results, JsonDocument.Parse(line).RootElement), Is.True, $"a row of the results is not what its schema says: {line[..Math.Min(400, line.Length)]}");

            Assert.That(SmtSchemaCheck.IsValid(Schema("smt-report.schema.json"), JsonDocument.Parse(run.Report).RootElement), Is.True, "the report");
            Assert.That(SmtSchemaCheck.IsValid(Schema("smt-integrity.schema.json"), JsonDocument.Parse(run.Integrity).RootElement), Is.True, "the integrity document");
            Assert.That(SmtSchemaCheck.IsValid(Schema("smt-provenance.schema.json"), JsonDocument.Parse(run.Provenance).RootElement), Is.True, "the provenance");
            AssertWords(run.Results.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement),
                JsonDocument.Parse(run.Report).RootElement, JsonDocument.Parse(run.Integrity).RootElement);
        });
    }

    /// <summary>
    /// Holds the words of a run's documents that no schema enumerates to the lists of the
    /// capabilities document: the code of every row by its status, the reason of every partial or
    /// failed class, and the keys of the tables of the report and the integrity document that are
    /// codes. A word a document says and the capabilities do not list fails.
    /// </summary>
    /// <param name="rows">The rows of the results.</param>
    /// <param name="report">The report.</param>
    /// <param name="integrity">The integrity document.</param>
    public static void AssertWords(IEnumerable<JsonElement> rows, JsonElement report, JsonElement integrity)
    {
        using var document = JsonDocument.Parse(SmtDiagnosticsRig.Capabilities(new SmtRuntimeDiagnosticsData { ModulePresent = true }));
        var capabilities = document.RootElement;
        var codes = capabilities.GetProperty("results").GetProperty("codes");
        var reasons = List(capabilities.GetProperty("results").GetProperty("validation_reasons"));

        foreach (var row in rows)
        {
            var status = row.GetProperty("status").GetString()!;
            var code = row.GetProperty("code").GetString()!;
            if (code.Length > 0)
                Assert.That(codes.TryGetProperty(status, out var listed) ? List(listed) : [], Does.Contain(code), $"the code of a row {status} is listed in the capabilities");

            var reason = row.GetProperty("validation_reason").GetString()!;
            if (reason.Length > 0)
                Assert.That(reasons, Does.Contain(reason), "the reason of a class is listed in the capabilities");
        }

        Assert.That(Keys(report.GetProperty("problem_set").GetProperty("refused_by_code")), Is.SubsetOf(List(codes.GetProperty("refused"))), "the report's refused_by_code");
        Assert.That(Keys(report.GetProperty("validation").GetProperty("partial_by_reason")), Is.SubsetOf(reasons), "the report's partial_by_reason");
        Assert.That(Keys(integrity.GetProperty("summary").GetProperty("no_result_by_reason")), Is.SubsetOf(List(codes.GetProperty("no_result"))), "the integrity document's no_result_by_reason");
        Assert.That(Keys(integrity.GetProperty("summary").GetProperty("repairs_by_reason")), Is.SubsetOf(List(capabilities.GetProperty("run").GetProperty("repair_reasons"))), "the integrity document's repairs_by_reason");
    }

    /// <summary>Holds a run's documents to name no machine: not the test host's name, its user, or a folder of it.</summary>
    /// <param name="run">The run.</param>
    public static void AssertNamesNoMachine(SmtRunDocuments run)
    {
        var text = run.AllText;

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain(Environment.MachineName).IgnoreCase, "no document names the machine");
            Assert.That(text, Does.Not.Contain(Environment.UserName).IgnoreCase, "or its user");
            Assert.That(text, Does.Not.Contain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)).IgnoreCase, "or a folder of it");
            foreach (var ticket in Tickets(run))
                Assert.That(text, Does.Not.Contain(ticket), "or a package's ticket");
        });
    }

    /// <summary>The tickets of every package the run gave out.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The tickets.</returns>
    public static List<string> Tickets(SmtRunDocuments run)
    {
        var tickets = new List<string>();
        foreach (var name in new[] { "packages", "repairs", "disputes" })
        {
            if (run.Job.Variables[name].Value is IEnumerable<SmtTaskPackageData> packages)
                tickets.AddRange(packages.Select(package => package.Ticket));
        }

        return tickets;
    }

    /// <summary>A row of the results in a few words, as <see cref="SmtNodeRig.Words"/> writes a node's row.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The words.</returns>
    public static string Words(JsonElement row)
    {
        var status = row.GetProperty("status").GetString()!;
        var code = row.GetProperty("code").GetString()!;
        if (status != SmtResultWords.OK)
            return code.Length > 0 ? $"{status} {code}" : status;

        var words = $"ok {row.GetProperty("verdict").GetString()}";
        var reason = row.GetProperty("reason").GetString();
        if (reason != SmtResultWords.NONE)
            words += $" {reason}";

        if (row.GetProperty("query").GetString() == "enumerate")
            words += $" {row.GetProperty("count").GetInt32()} {(row.GetProperty("complete").GetBoolean() ? "complete" : row.GetProperty("limit_reached").GetBoolean() ? "limit" : "incomplete")}";

        return row.TryGetProperty("proof", out var proof) && proof.GetString() != SmtResultWords.NONE ? $"{words} {proof.GetString()}" : words;
    }

    private static List<string> List(JsonElement words)
    {
        return words.EnumerateArray().Select(word => word.GetString()!).ToList();
    }

    private static List<string> Keys(JsonElement table)
    {
        return table.EnumerateObject().Select(entry => entry.Name).ToList();
    }

    #endregion
}
