using System.Globalization;
using System.Text;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The golden file of the solver's outcomes, <c>Fixtures/Solver/outcomes.tsv</c>: for each case of
/// <see cref="SmtSolverCases"/> the digest of its whole outcome (<see cref="SmtSolved.Digest"/>:
/// the verdict, the models, the counts, the transcript, everything but the wall time) and its
/// fuel. One file for every platform: it is written once, on one machine, and every other
/// machine is held to it.
/// </summary>
internal static class SmtSolverGolden
{
    #region Constants

    /// <summary>The file, relative to the test project and to the tests' output folder.</summary>
    public const string FILE = "Fixtures/Solver/outcomes.tsv";

    private const char SEPARATOR = '\t';
    private const char COMMENT = '#';
    private const string HEADER = "id\tsha256\tfuel";

    private static readonly string[] PREAMBLE =
    [
        "# The outcomes of the solver's test problems (Mock/SmtSolverCases.cs) under the pinned solver module:",
        "# the SHA-256 of each whole outcome but its wall time, and its fuel. The same on every platform and in",
        "# both trap modes; SmtSolverDeterminismTests holds every run to this file and says how it is rewritten."
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Reads the golden file from the tests' output folder.
    /// </summary>
    /// <returns>The digest and the fuel of each case, by the case's name, in the order of the file.</returns>
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

    /// <summary>
    /// Writes the golden file into the test project's own folder, from the outcomes of a run.
    /// </summary>
    /// <param name="outcomes">The outcome of each case, in the order of the cases.</param>
    /// <returns>The file that was written.</returns>
    /// <exception cref="InvalidOperationException">The tests do not run under their project's folder.</exception>
    public static string Write(IEnumerable<(string Id, SmtSolved Outcome)> outcomes)
    {
        var project = SmtSolverModule.ProjectFolder() ?? throw new InvalidOperationException("the test project's folder was not found: the golden file is written into it");
        var path = Path.Combine(project, FILE.Replace('/', Path.DirectorySeparatorChar));

        var text = new StringBuilder();
        foreach (var line in PREAMBLE)
            text.Append(line).Append('\n');

        text.Append(HEADER).Append('\n');
        foreach (var (id, outcome) in outcomes)
            text.Append(id).Append(SEPARATOR).Append(outcome.Digest).Append(SEPARATOR).Append(outcome.Fuel.ToString(CultureInfo.InvariantCulture)).Append('\n');

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    #endregion
}
