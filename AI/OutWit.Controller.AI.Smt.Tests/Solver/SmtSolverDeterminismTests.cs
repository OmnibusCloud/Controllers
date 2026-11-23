using System.Reflection;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The point of the whole design: the same problem, options and solver module give the same
/// outcome to the last byte (the verdict, the models, the counts, the fuel, the transcript) on
/// every machine, in both trap modes, however many sessions run beside it.
/// <para>
/// The proof across machines is one committed file, <c>Fixtures/Solver/outcomes.tsv</c>
/// (<see cref="SmtSolverGolden"/>): the digest of the whole outcome and the fuel of each case of
/// <see cref="SmtSolverCases"/>. The file was written once, on one machine; the workflow runs
/// this fixture on Windows, Linux and macOS, and each of them must give exactly that file.
/// </para>
/// <para>
/// The file is rewritten on purpose only, when a change is meant to move the outcomes (another
/// solver module, another command in a session, another case): run this fixture with the
/// environment variable <see cref="WRITE_VARIABLE"/> set to <c>1</c>. The file in the test
/// project's folder is then written from the run and the golden test is inconclusive, so that
/// no run that wrote the file passes for a run that was held to it. Read the difference, and
/// commit the file with the change that explains it.
/// </para>
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtSolverDeterminismTests
{
    #region Constants

    /// <summary>Set to <c>1</c>, makes the golden test write the golden file instead of holding the run to it.</summary>
    public const string WRITE_VARIABLE = "WITAI_SMT_WRITE_GOLDEN";

    private const int THREADS = 8;

    #endregion

    #region Fields

    private Dictionary<string, SmtSolved> m_outcomes = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();

        var compile = SmtSolverRig.Warm();
        Assert.That(SmtSolverRig.Unavailable(), Is.Null, "this process has a sandbox for the solver");
        TestContext.Progress.WriteLine($"the solver module compiled in {compile} ms in this process");

        m_outcomes = Run(null);
        foreach (var one in SmtSolverCases.All)
            TestContext.Progress.WriteLine($"{one.Id,-28} {m_outcomes[one.Id].Digest} fuel {m_outcomes[one.Id].Fuel,12} {m_outcomes[one.Id].WallMs,5} ms  {m_outcomes[one.Id].Words()}");
    }

    #endregion

    #region Golden Tests

    [Test]
    public void EveryOutcomeIsTheOneOfTheGoldenFileTest()
    {
        if (Environment.GetEnvironmentVariable(WRITE_VARIABLE) == "1")
        {
            var written = SmtSolverGolden.Write(SmtSolverCases.All.Select(one => (one.Id, m_outcomes[one.Id])));
            Assert.Inconclusive($"the golden file was written from this run, not compared with it: {written}");
        }

        var golden = SmtSolverGolden.Read();

        Assert.That(golden.Select(line => line.Id), Is.EqualTo(SmtSolverCases.Ids), $"{SmtSolverGolden.FILE} names every case, in the order of the cases");
        Assert.Multiple(() =>
        {
            foreach (var (id, digest, fuel) in golden)
            {
                var outcome = m_outcomes[id];
                if (outcome.Digest != digest || outcome.Fuel != fuel)
                    TestContext.Out.WriteLine($"--- {id}, as this machine has it:\n{outcome.Canonical}");

                Assert.That(outcome.Fuel, Is.EqualTo(fuel), $"{id}: the fuel");
                Assert.That(outcome.Digest, Is.EqualTo(digest), $"{id}: the digest of the whole outcome ({outcome.Words()})");
            }
        });
    }

    [Test]
    public void TheDigestCoversEveryValueOfAnOutcomeButItsWallTimeTest()
    {
        // The digest is written out value by value (SmtSolverRig.Canonical): a value added to the
        // controller's outcome must be added there, or the golden file would not see it move.
        var properties = typeof(SmtSolveOutcome).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(properties, Has.Count.EqualTo(SmtSolverRig.OUTCOME_PROPERTIES), string.Join(", ", properties));
            Assert.That(properties, Does.Contain(nameof(SmtSolveOutcome.WallMs)));

            // And the wall time is not in it: the same outcome with another time has the same text.
            var one = new SmtSolveOutcome { Transcript = "> (check-sat)\nsat\n", Fuel = 7, WallMs = 5 };
            var other = new SmtSolveOutcome { Transcript = "> (check-sat)\nsat\n", Fuel = 7, WallMs = 500 };
            var burned = new SmtSolveOutcome { Transcript = "> (check-sat)\nsat\n", Fuel = 8, WallMs = 5 };
            Assert.That(SmtSolverRig.Canonical(other), Is.EqualTo(SmtSolverRig.Canonical(one)));
            Assert.That(SmtSolverRig.Canonical(burned), Is.Not.EqualTo(SmtSolverRig.Canonical(one)));
        });
    }

    #endregion

    #region Same Run Tests

    [Test]
    public void EveryCaseEndsAsItIsWrittenTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var one in SmtSolverCases.All)
                Assert.That(m_outcomes[one.Id].Words(), Is.EqualTo(one.Expected), $"{one.Id}: {m_outcomes[one.Id]}");
        });
    }

    [Test]
    public void TwoRunsInARowGiveTheSameOutcomesTest()
    {
        AssertSame(Run(null), "a second run");
    }

    [Test]
    public void ExplicitTrapChecksGiveTheSameOutcomesAsHardwareTrapsTest()
    {
        if (SmtSolverRig.ExplicitByDefault())
            Assert.Ignore("this test process enforces shadow stacks, so its traps are explicit checks already: the two trap modes were NOT compared here");

        var compile = SmtSolverRig.WarmExplicit();
        TestContext.Progress.WriteLine($"the solver module compiled in {compile} ms with explicit trap checks");

        AssertSame(Run(new SmtSolverSetup { ExplicitTraps = true }), "explicit trap checks against hardware traps");
    }

    [Test]
    public void EightSessionsAtOnceGiveTheSameOutcomesAsOneByOneTest()
    {
        // Every thread solves every case, each starting at another one, so that every case runs
        // beside every other at some time.
        var cases = SmtSolverCases.All;
        var outcomes = new SmtSolved[THREADS][];
        var failures = new Exception?[THREADS];
        var threads = Enumerable.Range(0, THREADS).Select(thread => new Thread(() =>
        {
            try
            {
                var mine = new SmtSolved[cases.Count];
                for (var step = 0; step < cases.Count; step++)
                {
                    var index = (step + thread * cases.Count / THREADS) % cases.Count;
                    mine[index] = SmtSolverRig.Solve(cases[index].Problem, cases[index].Options);
                }

                outcomes[thread] = mine;
            }
            catch (Exception exception)
            {
                failures[thread] = exception;
            }
        })).ToList();

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        Assert.That(failures, Is.All.Null);
        Assert.Multiple(() =>
        {
            for (var thread = 0; thread < THREADS; thread++)
            {
                for (var index = 0; index < cases.Count; index++)
                    Assert.That(outcomes[thread][index].Canonical, Is.EqualTo(m_outcomes[cases[index].Id].Canonical), $"{cases[index].Id} on thread {thread} of {THREADS}");
            }
        });
    }

    #endregion

    #region Tools

    private static Dictionary<string, SmtSolved> Run(SmtSolverSetup? setup)
    {
        return SmtSolverCases.All.ToDictionary(
            one => one.Id,
            one => setup == null ? SmtSolverRig.Solve(one.Problem, one.Options) : SmtSolverRig.Solve(one.Problem, one.Options, setup));
    }

    private void AssertSame(Dictionary<string, SmtSolved> other, string what)
    {
        Assert.Multiple(() =>
        {
            foreach (var one in SmtSolverCases.All)
                Assert.That(other[one.Id].Canonical, Is.EqualTo(m_outcomes[one.Id].Canonical), $"{one.Id}: {what}");
        });
    }

    #endregion
}
