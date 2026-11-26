using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Tests.Delivery;

/// <summary>
/// The row a node returns for a problem says nothing of the machine but its platform and the
/// controller's version: the wall time of the session, which no digest and no document reads and
/// which would tell a fast machine from a slow one across jobs, stays on the machine.
/// </summary>
[TestFixture]
public sealed class SmtResultRowsTests
{
    #region Constants

    private const string TICKET = "0123456789abcdef0123456789abcdef";

    #endregion

    #region Row Tests

    [Test]
    public void ARowSaysNoWallTimeTest()
    {
        var problem = new SmtProblemData { Index = 3, Id = "p3", Query = SmtQuery.Check, Text = "(declare-const x Int)\n(assert (> x 0))\n" };
        var outcome = new SmtSolveOutcome
        {
            Status = SmtSolveStatus.Ok,
            Query = SmtQuery.Check,
            Verdict = SmtVerdict.Unsat,
            Checks = 1,
            ResourceCount = 1_234,
            Fuel = 5_678,
            PeakMemoryBytes = 1 << 20,
            Transcript = "> (check-sat)\nunsat\n",
            WallMs = 4_321
        };

        var row = SmtResultRows.Of(problem, outcome, TICKET);
        var timed = row.Clone();
        timed.WallMs = outcome.WallMs;

        Assert.Multiple(() =>
        {
            Assert.That(row.WallMs, Is.Zero, "the session's wall time is not written");
            Assert.That(row.Fuel, Is.EqualTo(5_678), "what the session cost in counts is");
            Assert.That(row.Platform, Is.EqualTo(SmtResultRows.Platform));
            Assert.That(SmtResultDigest.Holds(row), Is.True);
            Assert.That(SmtResultDigest.Of(timed), Is.EqualTo(row.Digest), "the digest never read the wall time: a row's digest is what it was");
        });
    }

    #endregion
}
