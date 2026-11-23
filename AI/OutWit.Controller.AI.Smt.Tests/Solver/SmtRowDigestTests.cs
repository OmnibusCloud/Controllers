using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// What the host compares copies by: the digest of a row (<c>SmtResultData.Digest</c>). The same
/// problem gives the same digest twice in a row, in both trap modes, and solved by the pool of
/// eight sessions as a package is; and on every platform, which one committed file holds,
/// <c>Fixtures/Solver/rows.tsv</c> (<see cref="SmtRowGolden"/>): the workflow runs this fixture on
/// Windows, Linux and macOS, and each must give exactly that file.
/// <para>
/// The file is rewritten on purpose only (another solver module, another command in a session,
/// another case, another canonical form of a row): run this fixture with the environment variable
/// <see cref="WRITE_VARIABLE"/> set to <c>1</c>; the golden test is then inconclusive.
/// </para>
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRowDigestTests
{
    #region Constants

    /// <summary>Set to <c>1</c>, makes the golden test write the golden file instead of holding the run to it.</summary>
    public const string WRITE_VARIABLE = "WITAI_SMT_WRITE_ROWS";

    private const int POOL_DEGREE = 8;

    #endregion

    #region Fields

    private Dictionary<int, SmtResultData> m_rows = null!;

    private string m_folder = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
        Assert.That(SmtSolverRig.Unavailable(), Is.Null, "this process has a sandbox for the solver");

        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_rows_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_folder);

        m_rows = Rows(null);
        foreach (var one in SmtSolverCases.All.Select((one, index) => (one.Id, Index: index)))
            TestContext.Progress.WriteLine($"{one.Id,-28} {m_rows[one.Index].Digest} {SmtNodeRig.Words(m_rows[one.Index])}");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Golden Tests

    [Test]
    public void EveryRowDigestIsTheOneOfTheGoldenFileTest()
    {
        if (Environment.GetEnvironmentVariable(WRITE_VARIABLE) == "1")
        {
            var written = SmtRowGolden.Write(SmtSolverCases.All.Select((one, index) => (one.Id, index, m_rows[index].Digest)));
            Assert.Inconclusive($"the golden file was written from this run, not compared with it: {written}");
        }

        var golden = SmtRowGolden.Read();

        Assert.That(golden.Select(line => line.Id), Is.EqualTo(SmtSolverCases.Ids), $"{SmtRowGolden.FILE} names every case, in the order of the cases");
        Assert.Multiple(() =>
        {
            foreach (var (id, index, digest) in golden)
            {
                if (m_rows[index].Digest != digest)
                    TestContext.Out.WriteLine($"--- {id}, as this machine has it:\n{SmtResultDigest.Canonical(m_rows[index])}");

                Assert.That(m_rows[index].Digest, Is.EqualTo(digest), $"{id}: the digest of its row ({SmtNodeRig.Words(m_rows[index])})");
            }
        });
    }

    [Test]
    public void EveryRowCarriesTheDigestOfItsOwnValuesAndSaysWhatTheSessionSaidTest()
    {
        Assert.Multiple(() =>
        {
            for (var index = 0; index < SmtSolverCases.All.Count; index++)
            {
                var one = SmtSolverCases.All[index];
                var row = m_rows[index];

                Assert.That(SmtResultDigest.Holds(row), Is.True, one.Id);
                Assert.That(SmtNodeRig.Words(row), Is.EqualTo(one.Expected), one.Id);
                Assert.That((row.Index, row.Id, row.Query), Is.EqualTo((index, one.Id, one.Problem.Query)), one.Id);
                Assert.That(row.Hash, Is.EqualTo(Sha256(one.Problem.Text)), $"{one.Id}: the hash of the text the node was given");
                Assert.That(row.Ticket, Is.EqualTo(SmtNodeRig.TICKET));
                Assert.That(row.ControllerVersion, Is.EqualTo(SmtNodeRig.ControllerVersion));
                Assert.That(row.Platform, Is.AnyOf("win-x64", "linux-x64", "osx-arm64"));
                Assert.That(SmtResultWords.STATUSES, Does.Contain(row.Status));
                Assert.That(SmtResultWords.VERDICTS, Does.Contain(row.Verdict));
                Assert.That(SmtResultWords.REASONS, Does.Contain(row.Reason));
                Assert.That(SmtResultWords.PROOFS, Does.Contain(row.Proof));
            }
        });
    }

    [Test]
    public void TheControllerWritesTheWordsOfTheModelPackageTest()
    {
        var (statuses, verdicts, reasons, proofs) = SmtNodeRig.ControllerWords();

        Assert.Multiple(() =>
        {
            Assert.That(statuses, Is.EquivalentTo(SmtResultWords.STATUSES));
            Assert.That(verdicts, Is.EquivalentTo(SmtResultWords.VERDICTS));
            Assert.That(reasons, Is.EquivalentTo(SmtResultWords.REASONS));
            Assert.That(proofs, Is.EquivalentTo(SmtResultWords.PROOFS));
        });
    }

    #endregion

    #region Same Digest Tests

    [Test]
    public void TwoRunsInARowGiveTheSameDigestsTest()
    {
        AssertSame(Rows(null), "a second run");
    }

    [Test]
    public void ExplicitTrapChecksGiveTheSameDigestsAsHardwareTrapsTest()
    {
        if (SmtSolverRig.ExplicitByDefault())
            Assert.Ignore("this test process enforces shadow stacks, so its traps are explicit checks already: the two trap modes were NOT compared here");

        SmtSolverRig.WarmExplicit();
        AssertSame(Rows(new SmtSolverSetup { ExplicitTraps = true }), "explicit trap checks against hardware traps");
    }

    [Test]
    public void ThePoolOfEightSessionsGivesTheSameDigestsAsOneByOneTest()
    {
        // The golden problems as packages, a package for each set of options, each solved by the
        // pool at eight sessions at once, its rows written as they end and read back.
        var pooled = new Dictionary<int, SmtResultData>();
        var groups = SmtGoldenPackages.Groups();
        for (var group = 0; group < groups.Count; group++)
        {
            var (options, problems) = groups[group];
            var path = Path.Combine(m_folder, $"golden-{group}.wsp");
            var package = SmtNodeRig.Write(problems, options, path);
            package.Ticket = SmtNodeRig.TICKET;

            foreach (var row in SmtNodeRig.RunPackage(package, path, POOL_DEGREE, Path.Combine(m_folder, $"golden-{group}.rows")))
                pooled.Add(row.Index, row);
        }

        AssertSame(pooled, $"the pool at {POOL_DEGREE} sessions at once");
    }

    #endregion

    #region Tools

    private static Dictionary<int, SmtResultData> Rows(SmtSolverSetup? setup)
    {
        var rows = new Dictionary<int, SmtResultData>();
        for (var index = 0; index < SmtSolverCases.All.Count; index++)
        {
            var one = SmtSolverCases.All[index];
            rows[index] = SmtNodeRig.Row(SmtGoldenPackages.Indexed(one.Problem, index), one.Options, SmtNodeRig.TICKET, setup);
        }

        return rows;
    }

    private void AssertSame(Dictionary<int, SmtResultData> other, string what)
    {
        Assert.That(other.Keys.Order(), Is.EqualTo(m_rows.Keys.Order()), what);
        Assert.Multiple(() =>
        {
            foreach (var (index, row) in m_rows)
                Assert.That(other[index].Digest, Is.EqualTo(row.Digest), $"{row.Id}: {what}");
        });
    }

    private static string Sha256(string text)
    {
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    #endregion
}
