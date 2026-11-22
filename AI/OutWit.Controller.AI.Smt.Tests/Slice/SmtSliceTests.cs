using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Slice;

/// <summary>
/// The sanitizer on real problems, without a byte of them in the repository: the committed list
/// <c>Slice/fetch-list.tsv</c> names 469 problems of the SMT-LIB release 2025 with their hashes
/// and expected verdicts, <c>Slice/fetch.py</c> puts them into the test project's
/// <c>@Data/slice</c> (gitignored; another folder can be named by <c>WITAI_SMT_SLICE_DIR</c>).
/// <para>
/// Every problem that is there is the file the list names (by its SHA-256), is sanitized and
/// held to the properties of a problem; its <c>:status</c> is the list's verdict. The problems
/// the sanitizer refuses are written out below, by code: the number is part of the test, so a
/// rule that starts or stops refusing real problems shows here.
/// </para>
/// <para>
/// Without the folder the tests are ignored, with the reason; on the build server they fail,
/// and so does a single missing problem there: a green run has read all of them.
/// </para>
/// </summary>
[TestFixture]
[Category("Slice")]
public class SmtSliceTests
{
    #region Constants

    /// <summary>Names another folder that holds the fetched problems.</summary>
    public const string FOLDER_VARIABLE = "WITAI_SMT_SLICE_DIR";

    private const string LISTING = "Slice/fetch-list.tsv";
    private const string CI_ENV = "GITHUB_ACTIONS";
    private const string PROJECT_FILE = "OutWit.Controller.AI.Smt.Tests.csproj";
    private const int PROBLEMS = 469;

    /// <summary>
    /// The problems of the slice the sanitizer refuses, by code: the ids of the list. None: all
    /// 469 are taken (the deepest nests its lists 11,033 levels deep).
    /// </summary>
    private static readonly Dictionary<string, string[]> REFUSED = new(StringComparer.Ordinal);

    #endregion

    #region Slice Tests

    [Test]
    public void ListNamesTheWholeSliceTest()
    {
        var rows = Listing();

        Assert.That(rows, Has.Count.EqualTo(PROBLEMS));
        Assert.That(rows.Select(row => row.Id), Is.Unique);
        Assert.That(rows.Select(row => row.Status), Is.All.AnyOf(SmtProblemStatus.Sat, SmtProblemStatus.Unsat));
        Assert.That(REFUSED.Values.SelectMany(ids => ids), Is.SubsetOf(rows.Select(row => row.Id)));
        Assert.That(REFUSED.Keys, Is.SubsetOf(SmtRefusalCodes.All));
    }

    [Test]
    public void EveryProblemOfTheSliceIsTakenOrRefusedAsWrittenHereTest()
    {
        var rows = Listing();
        var folder = RequireFolder();
        var onBuildServer = OnBuildServer();
        var expected = REFUSED.SelectMany(pair => pair.Value.Select(id => (Id: id, Code: pair.Key))).ToDictionary(pair => pair.Id, pair => pair.Code, StringComparer.Ordinal);
        var refused = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var present = 0;
        var accepted = 0;
        var stripped = 0;
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        Assert.Multiple(() =>
        {
            foreach (var row in rows)
            {
                var path = Path.Combine(folder, row.Id + ".smt2");
                if (!File.Exists(path))
                {
                    if (onBuildServer)
                        Assert.Fail($"{row.Id} is not in {folder}: the slice was not fetched whole");

                    continue;
                }

                present++;
                var bytes = File.ReadAllBytes(path);
                if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != row.Sha256)
                {
                    Assert.Fail($"{row.Id} in {folder} is not the file the list names: its SHA-256 differs");
                    continue;
                }

                var text = strict.GetString(bytes);
                var result = SmtSanitizer.Sanitize(text);
                if (result.Refusal != null)
                {
                    SmtSanitizerChecks.HoldRefusal(result.Refusal);
                    if (!refused.TryGetValue(result.Refusal.Code, out var ids))
                        refused[result.Refusal.Code] = ids = [];

                    ids.Add(row.Id);
                    Assert.That(expected.GetValueOrDefault(row.Id), Is.EqualTo(result.Refusal.Code), $"{row.Id} is refused, line {result.Refusal.Line}: {result.Refusal.Reason}");
                    continue;
                }

                accepted++;
                stripped += result.Problem!.Stripped.Values.Sum();
                Assert.That(expected.ContainsKey(row.Id), Is.False, $"{row.Id} is written here as refused, and is taken");
                Assert.That(result.Problem.Status, Is.EqualTo(row.Status), $"{row.Id}: the :status is not the list's verdict");
                Assert.That(result.Problem.Logic, Is.EqualTo(row.Logic), $"{row.Id}: the logic is not the list's");
                Assert.That(result.Problem.AssertionCount, Is.GreaterThan(0), row.Id);
                SmtSanitizerChecks.HoldProperties(result.Problem);

                // The collection's files end their lines with a line feed, and most carry a source
                // of several lines: the same file as a checkout with carriage returns has it is
                // the same problem.
                var windows = SmtSanitizer.Sanitize(text.Replace("\n", "\r\n"));
                Assert.That(windows.Problem?.Hash, Is.EqualTo(result.Problem.Hash), $"{row.Id}: with carriage returns before its line feeds the file is another problem");
            }

            // The report of the run, whatever failed above: what was there, and the refusals by code.
            TestContext.Out.WriteLine($"{present} of {rows.Count} problems present; {accepted} taken ({stripped} commands stripped), {present - accepted} refused");
            foreach (var (code, ids) in refused)
                TestContext.Out.WriteLine($"  {code}: {ids.Count} ({string.Join(" ", ids)})");

            if (present == rows.Count)
            {
                Assert.That(refused.ToDictionary(pair => pair.Key, pair => pair.Value.Count), Is.EquivalentTo(REFUSED.ToDictionary(pair => pair.Key, pair => pair.Value.Length)), "the refusals of the whole slice, by code");
                Assert.That(accepted, Is.EqualTo(PROBLEMS - REFUSED.Values.Sum(ids => ids.Length)));
            }
        });
    }

    #endregion

    #region Tools

    /// <summary>The list as committed: the id, the logic, the SHA-256 and the expected verdict of each problem.</summary>
    private static List<(string Id, string Logic, string Sha256, SmtProblemStatus Status)> Listing()
    {
        var lines = File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory, LISTING));
        var columns = lines[0].Split('\t');
        var id = Array.IndexOf(columns, "id");
        var logic = Array.IndexOf(columns, "logic");
        var sha256 = Array.IndexOf(columns, "sha256");
        var status = Array.IndexOf(columns, "status");

        return lines.Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .Select(cells => (cells[id], cells[logic], cells[sha256], cells[status] switch
            {
                "sat" => SmtProblemStatus.Sat,
                "unsat" => SmtProblemStatus.Unsat,
                _ => SmtProblemStatus.None
            }))
            .ToList();
    }

    /// <summary>The folder of the fetched problems; ignores the calling test when it is not there (fails it on the build server).</summary>
    private static string RequireFolder()
    {
        var folder = Environment.GetEnvironmentVariable(FOLDER_VARIABLE) is { Length: > 0 } named ? named : DefaultFolder();
        if (folder != null && Directory.Exists(folder))
            return folder;

        var message = $"the slice is not on this machine ({folder ?? "the test project's folder was not found"}): Slice/fetch.py fetches it";
        if (OnBuildServer())
            Assert.Fail(message);

        Assert.Ignore(message);
        return "";
    }

    private static bool OnBuildServer()
    {
        return string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DefaultFolder()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, PROJECT_FILE)))
            directory = directory.Parent;

        return directory == null ? null : Path.Combine(directory.FullName, "@Data", "slice");
    }

    #endregion
}
