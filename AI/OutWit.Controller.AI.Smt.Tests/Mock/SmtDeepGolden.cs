using System.Globalization;
using System.Text;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The golden file of the deep problems, <c>Fixtures/Solver/deep.tsv</c>: for each case of
/// <see cref="SmtDeepProblems"/> the digest of the row a node returns for it
/// (<c>SmtResultData.Digest</c>) and its fuel. One file for every platform: it is written once, on
/// one machine, and every other machine is held to it.
/// <para>
/// These cases have a file of their own, beside the golden rows of the solver's other test
/// problems: those also run through whole runs, where every model is held to the host's
/// evaluator, and the evaluator has a depth of its own that a term nested this deep passes.
/// </para>
/// </summary>
internal static class SmtDeepGolden
{
    #region Constants

    /// <summary>The file, relative to the test project and to the tests' output folder.</summary>
    public const string FILE = "Fixtures/Solver/deep.tsv";

    private const char SEPARATOR = '\t';
    private const char COMMENT = '#';
    private const string HEADER = "id\tdigest\tfuel";

    private static readonly string[] PREAMBLE =
    [
        "# The rows a node returns for the problems nested as deep as the reader takes them (Mock/SmtDeepProblems.cs):",
        "# the digest of each row (SmtResultData.Digest), which leaves the wall time, the platform, the controller",
        "# version and the ticket out, and its fuel. The same on every platform and in both trap modes;",
        "# SmtSolverDeepNestingTests holds every run to this file and says how it is rewritten."
    ];

    #endregion

    #region Functions

    /// <summary>Reads the golden file from the tests' output folder.</summary>
    /// <returns>The digest and the fuel of each case, by its name, in the order of the file.</returns>
    public static IReadOnlyList<(string Id, string Digest, long Fuel)> Read()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, FILE.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return [];

        return File.ReadAllLines(path)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0 && line[0] != COMMENT && line != HEADER)
            .Select(line => line.Split(SEPARATOR))
            .Select(cells => (cells[0], cells[1], long.Parse(cells[2], CultureInfo.InvariantCulture)))
            .ToList();
    }

    /// <summary>Writes the golden file into the test project's own folder.</summary>
    /// <param name="rows">The id, the digest and the fuel of each case, in the order of the cases.</param>
    /// <returns>The file that was written.</returns>
    /// <exception cref="InvalidOperationException">The tests do not run under their project's folder.</exception>
    public static string Write(IEnumerable<(string Id, string Digest, long Fuel)> rows)
    {
        var project = SmtSolverModule.ProjectFolder() ?? throw new InvalidOperationException("the test project's folder was not found: the golden file is written into it");
        var path = Path.Combine(project, FILE.Replace('/', Path.DirectorySeparatorChar));

        var text = new StringBuilder();
        foreach (var line in PREAMBLE)
            text.Append(line).Append('\n');

        text.Append(HEADER).Append('\n');
        foreach (var (id, digest, fuel) in rows)
            text.Append(id).Append(SEPARATOR).Append(digest).Append(SEPARATOR).Append(fuel.ToString(CultureInfo.InvariantCulture)).Append('\n');

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    #endregion
}
