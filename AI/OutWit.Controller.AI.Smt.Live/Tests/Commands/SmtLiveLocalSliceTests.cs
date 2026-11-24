using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OutWit.Controller.AI.Smt.Live.Commands;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Local;
using OutWit.Controller.AI.Smt.Live.Pack;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Commands;

/// <summary>
/// <c>local</c> on a handful of real SMT-LIB problems: the two fastest of each answer in QF_LIA
/// and QF_BV of the slice the controller's tests solve (their committed list names each problem
/// by its path, its SHA-256 and its <c>:status</c>; the problems are never committed: the job
/// "slice" of the workflow fetches them into the controller tests' <c>@Data/slice</c>). They are
/// packed by <c>pack</c> and run by <c>local</c>; every verdict must be the list's.
/// </summary>
[TestFixture]
[Category("Slice")]
[NonParallelizable]
public sealed class SmtLiveLocalSliceTests
{
    #region Constants

    private const int PER_ANSWER = 2;

    private static readonly string[] LOGICS = ["QF_LIA", "QF_BV"];
    private static readonly string[] ANSWERS = ["sat", "unsat"];

    #endregion

    #region Fields

    private string m_root = null!;
    private SmtLiveSandbox m_sandbox = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        SmtLiveSolver.Require();
        m_root = SmtLiveFixtures.NewFolder();

        var sandbox = SmtLiveSandbox.Open(Path.Combine(m_root, "sandbox"), out var problem);
        if (sandbox == null)
            SmtLiveSolver.Missing($"the controller's sandbox cannot start here: {problem}");

        m_sandbox = sandbox!;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        m_sandbox?.Dispose();
        if (m_root != null)
            SmtLiveFixtures.Delete(m_root);
    }

    #endregion

    #region Tests

    [Test]
    public async Task AHandfulOfTheSliceGetsTheVerdictsOfTheListTest()
    {
        var tests = Path.Combine(SmtLiveFixtures.RepositoryRoot(), "AI", "OutWit.Controller.AI.Smt.Tests");
        var slice = Path.Combine(tests, "@Data", "slice");
        var chosen = Choose(Path.Combine(tests, "Slice", "fetch-list.tsv"));

        var folder = Path.Combine(m_root, "slice");
        Directory.CreateDirectory(folder);
        foreach (var (id, _, sha256) in chosen)
        {
            var source = Path.Combine(slice, id + ".smt2");
            if (!File.Exists(source))
                SmtLiveSolver.Missing($"the slice is not in {slice}: Slice/fetch.py of the controller's tests fetches it");

            Assert.That(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))), Is.EqualTo(sha256), $"{id}: the file the list names");
            File.Copy(source, Path.Combine(folder, id + ".smt2"));
        }

        var output = new StringWriter();
        var set = Path.Combine(m_root, "slice.jsonl");
        var packed = new SmtLivePack(output).Run(folder, null, set);

        var run = Path.Combine(m_root, "slice-run");
        var exit = await new SmtLiveLocal(output, SmtLiveClock.StartNow(), m_sandbox).RunAsync(set, new SmtOptionsData(), null, run, CancellationToken.None);
        TestContext.Out.WriteLine(output.ToString());

        var rows = File.ReadAllLines(Path.Combine(run, SmtLiveDocuments.RESULTS.File)).Where(line => line.Length > 0)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToDictionary(row => (string)row["id"]!);
        Assert.Multiple(() =>
        {
            Assert.That(packed, Is.EqualTo(SmtLiveExitCodes.OK), "every file packed and taken");
            Assert.That(exit, Is.EqualTo(SmtLiveExitCodes.OK));
            Assert.That(rows, Has.Count.EqualTo(chosen.Count));
            foreach (var (id, status, _) in chosen)
            {
                Assert.That((string?)rows[id]["status"], Is.EqualTo("ok"), id);
                Assert.That((string?)rows[id]["verdict"], Is.EqualTo(status), $"{id}: the list's verdict");
                Assert.That((string?)rows[id]["expected_agreement"], Is.EqualTo("agree"), id);
                Assert.That((string?)rows[id]["digest"], Has.Length.EqualTo(64), id);
            }
        });
    }

    #endregion

    #region Tools

    /// <summary>The fastest problems of each answer in each logic, by the native solve time the list states.</summary>
    private static List<(string Id, string Status, string Sha256)> Choose(string list)
    {
        var lines = File.ReadAllLines(list);
        var header = lines[0].Split('\t').ToList();
        int Column(string name) => header.IndexOf(name);

        var rows = lines.Skip(1).Where(line => line.Length > 0).Select(line => line.Split('\t')).ToList();
        return LOGICS.SelectMany(logic => ANSWERS.SelectMany(answer => rows
                .Where(row => row[Column("logic")] == logic && row[Column("status")] == answer)
                .OrderBy(row => int.Parse(row[Column("native_ms")], System.Globalization.CultureInfo.InvariantCulture))
                .ThenBy(row => row[Column("id")], StringComparer.Ordinal)
                .Take(PER_ANSWER)
                .Select(row => (row[Column("id")], row[Column("status")], row[Column("sha256")]))))
            .ToList();
    }

    #endregion
}
