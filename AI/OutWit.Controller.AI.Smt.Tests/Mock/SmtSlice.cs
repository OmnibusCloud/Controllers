using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Slice;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The slice of real problems as the solver's tests read it: the committed list
/// <c>Slice/fetch-list.tsv</c> (ids, logics, hashes and expected verdicts; no problem text) and
/// the folder <c>Slice/fetch.py</c> puts the problems into, which is never committed
/// (<c>@Data/slice</c> under the test project, or the folder
/// <see cref="SmtSliceTests.FOLDER_VARIABLE"/> names).
/// </summary>
internal static class SmtSlice
{
    #region Constants

    /// <summary>The number of problems the list names.</summary>
    public const int PROBLEMS = 469;

    private const string LISTING = "Slice/fetch-list.tsv";
    private const string DATA_FOLDER = "@Data";
    private const string SLICE_FOLDER = "slice";
    private const string EXTENSION = ".smt2";
    private const char SEPARATOR = '\t';

    private static readonly UTF8Encoding STRICT = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    #endregion

    #region Functions

    /// <summary>The list as committed.</summary>
    /// <returns>The id, the logic, the SHA-256 and the expected verdict of each problem, in the order of the list.</returns>
    public static IReadOnlyList<(string Id, string Logic, string Sha256, SmtProblemStatus Status)> Listing()
    {
        var lines = File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory, LISTING.Replace('/', Path.DirectorySeparatorChar)));
        var columns = lines[0].Split(SEPARATOR);
        var id = Array.IndexOf(columns, "id");
        var logic = Array.IndexOf(columns, "logic");
        var sha256 = Array.IndexOf(columns, "sha256");
        var status = Array.IndexOf(columns, "status");

        return lines.Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(SEPARATOR))
            .Select(cells => (cells[id], cells[logic], cells[sha256], cells[status] switch
            {
                "sat" => SmtProblemStatus.Sat,
                "unsat" => SmtProblemStatus.Unsat,
                _ => SmtProblemStatus.None
            }))
            .ToList();
    }

    /// <summary>The folder of the fetched problems; ignores the calling test when it is not there (fails it on the build server).</summary>
    /// <returns>The folder.</returns>
    public static string RequireFolder()
    {
        var project = SmtSolverModule.ProjectFolder();
        var folder = Environment.GetEnvironmentVariable(SmtSliceTests.FOLDER_VARIABLE) is { Length: > 0 } named
            ? named
            : project == null ? null : Path.Combine(project, DATA_FOLDER, SLICE_FOLDER);

        if (folder != null && Directory.Exists(folder))
            return folder;

        var message = $"the slice is not on this machine ({folder ?? "the test project's folder was not found"}): Slice/fetch.py fetches it";
        if (SmtSolverModule.OnBuildServer)
            Assert.Fail(message);

        Assert.Ignore(message);
        return "";
    }

    /// <summary>
    /// Reads one problem of the slice, held to the hash the list names.
    /// </summary>
    /// <param name="folder">The folder of the fetched problems.</param>
    /// <param name="id">The problem's id in the list.</param>
    /// <param name="sha256">The SHA-256 the list names for it.</param>
    /// <returns>The problem's text; null when the file is not there.</returns>
    /// <exception cref="InvalidDataException">The file is not the one the list names.</exception>
    public static string? Read(string folder, string id, string sha256)
    {
        var path = Path.Combine(folder, id + EXTENSION);
        if (!File.Exists(path))
            return null;

        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != sha256)
            throw new InvalidDataException($"{id} in the slice's folder is not the file the list names: its SHA-256 differs");

        return STRICT.GetString(bytes);
    }

    #endregion
}
