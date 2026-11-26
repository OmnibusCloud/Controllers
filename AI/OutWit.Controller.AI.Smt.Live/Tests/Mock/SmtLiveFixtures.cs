using System.Text.Json.Nodes;
using OutWit.Controller.AI.Smt.Live.Documents;

namespace OutWit.Controller.AI.Smt.Live.Tests.Mock;

/// <summary>
/// The files of the tests: the documents of a small run and a capabilities document, each
/// holding to the controller's schema; folders of their own in the temporary folder; and edits of
/// a JSON document or of one row of JSON lines.
/// </summary>
internal static class SmtLiveFixtures
{
    #region Constants

    private const string FIXTURES = "Fixtures";
    private const string RUN = "run";
    private const string SOLUTION = "OutWit.slnx";

    #endregion

    #region Functions

    /// <summary>A document of the fixture run.</summary>
    /// <param name="document">The document.</param>
    /// <returns>Its path.</returns>
    public static string Run(SmtLiveDocument document)
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, FIXTURES, RUN, document.File);
    }

    /// <summary>The fixture capabilities document.</summary>
    /// <returns>Its path.</returns>
    public static string Capabilities()
    {
        return Path.Combine(TestContext.CurrentContext.TestDirectory, FIXTURES, SmtLiveDocuments.CAPABILITIES.File);
    }

    /// <summary>A new empty folder of the test's own.</summary>
    /// <returns>Its path.</returns>
    public static string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "smt-live-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Copies the four documents of the fixture run into a folder.</summary>
    /// <param name="folder">The folder; made when it is not there.</param>
    /// <returns>The folder.</returns>
    public static string CopyRun(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var document in SmtLiveDocuments.RUN)
            File.Copy(Run(document), Path.Combine(folder, document.File), true);

        return folder;
    }

    /// <summary>Edits a JSON document in place.</summary>
    /// <param name="path">The document.</param>
    /// <param name="edit">The edit.</param>
    public static void Edit(string path, Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(root);
        File.WriteAllText(path, root.ToJsonString());
    }

    /// <summary>Edits one row of a JSON lines file in place.</summary>
    /// <param name="path">The file.</param>
    /// <param name="row">The row's place, from 0.</param>
    /// <param name="edit">The edit.</param>
    public static void EditRow(string path, int row, Action<JsonObject> edit)
    {
        var lines = File.ReadAllLines(path).Where(line => line.Length > 0).ToList();
        var node = JsonNode.Parse(lines[row])!.AsObject();
        edit(node);
        lines[row] = node.ToJsonString();
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    /// <summary>
    /// Makes a copied run's folder the run of another set: its provenance and its measurement
    /// (<c>local.json</c> or <c>fleet.json</c>, when there is one) name the set given.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="sha256">The set's SHA-256.</param>
    /// <param name="bytes">Its bytes.</param>
    /// <param name="rows">Its rows.</param>
    public static void SetOfRun(string folder, string sha256, long bytes, long rows)
    {
        Edit(Path.Combine(folder, SmtLiveDocuments.PROVENANCE.File), root =>
        {
            root["problem_set"]!["sha256"] = sha256;
            root["problem_set"]!["bytes"] = bytes;
            root["problem_set"]!["rows"] = rows;
        });

        foreach (var measurement in new[] { "local.json", "fleet.json" })
        {
            var path = Path.Combine(folder, measurement);
            if (File.Exists(path))
                Edit(path, root => root["problem_set"]!["sha256"] = sha256);
        }
    }

    /// <summary>The repository's root: the folder that holds the solution.</summary>
    /// <returns>Its path.</returns>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, SOLUTION)))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, $"{SOLUTION} above the test folder");
        return directory!.FullName;
    }

    /// <summary>Deletes a folder of a test, whatever is in it.</summary>
    /// <param name="folder">The folder.</param>
    public static void Delete(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    #endregion
}
