using System.Globalization;
using System.Text;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The golden file of the rows, <c>Fixtures/Solver/rows.tsv</c>: for each case of
/// <see cref="SmtSolverCases"/> the digest of the row a node returns for it
/// (<c>SmtResultData.Digest</c>), with the case's place in the list as the problem's index. One file
/// for every platform: it is written once, on one machine, and every other machine is held to it.
/// </summary>
internal static class SmtRowGolden
{
    #region Constants

    /// <summary>The file, relative to the test project and to the tests' output folder.</summary>
    public const string FILE = "Fixtures/Solver/rows.tsv";

    private const char SEPARATOR = '\t';
    private const char COMMENT = '#';
    private const string HEADER = "id\tindex\tdigest";

    private static readonly string[] PREAMBLE =
    [
        "# The rows a node returns for the solver's test problems (Mock/SmtSolverCases.cs), each with its place in the list",
        "# as its index: the digest of each row (SmtResultData.Digest), which leaves the wall time, the platform, the",
        "# controller version and the ticket out. The same on every platform, in both trap modes and at any degree;",
        "# SmtRowDigestTests holds every run to this file and says how it is rewritten."
    ];

    #endregion

    #region Functions

    /// <summary>Reads the golden file from the tests' output folder.</summary>
    /// <returns>The index and the digest of each case, by its name, in the order of the file.</returns>
    public static IReadOnlyList<(string Id, int Index, string Digest)> Read()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, FILE.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return [];

        return File.ReadAllLines(path)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0 && line[0] != COMMENT && line != HEADER)
            .Select(line => line.Split(SEPARATOR))
            .Select(cells => (cells[0], int.Parse(cells[1], CultureInfo.InvariantCulture), cells[2]))
            .ToList();
    }

    /// <summary>Writes the golden file into the test project's own folder.</summary>
    /// <param name="rows">The id, index and digest of each case, in the order of the cases.</param>
    /// <returns>The file that was written.</returns>
    /// <exception cref="InvalidOperationException">The tests do not run under their project's folder.</exception>
    public static string Write(IEnumerable<(string Id, int Index, string Digest)> rows)
    {
        var project = SmtSolverModule.ProjectFolder() ?? throw new InvalidOperationException("the test project's folder was not found: the golden file is written into it");
        var path = Path.Combine(project, FILE.Replace('/', Path.DirectorySeparatorChar));

        var text = new StringBuilder();
        foreach (var line in PREAMBLE)
            text.Append(line).Append('\n');

        text.Append(HEADER).Append('\n');
        foreach (var (id, index, digest) in rows)
            text.Append(id).Append(SEPARATOR).Append(index.ToString(CultureInfo.InvariantCulture)).Append(SEPARATOR).Append(digest).Append('\n');

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    #endregion
}
