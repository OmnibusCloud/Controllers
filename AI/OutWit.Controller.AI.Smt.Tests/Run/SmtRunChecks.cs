using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Docs;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>What the tests of a run hold every run's documents to: their schemas, and that they name no machine.</summary>
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

    /// <summary>Holds every document of a run to its schema.</summary>
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
        });
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

    #endregion
}
