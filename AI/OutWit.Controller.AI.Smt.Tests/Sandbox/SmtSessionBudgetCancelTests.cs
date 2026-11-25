using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Sandbox;

/// <summary>
/// The ends of a package on the node's budget of sessions other than its rows: a job cancelled in
/// the middle of packages interleaved with another job's hands out nothing more, its running
/// sessions end, its packages return nothing and leave no file, a package of it that waited for
/// its turn never starts, and the other job's packages return all of their rows; a package that
/// fails (its blob ends before its problems) fails alone. The budget ends idle every time.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtSessionBudgetCancelTests : SmtSessionBudgetTestBase
{
    #region Constants

    private const int QUICK_MS = 60;

    #endregion

    #region Cancel Tests

    [Test]
    [CancelAfter(120_000)]
    public async Task ACancelInTheMiddleOfInterleavedPackagesEndsOnlyItsJobTest()
    {
        // Two jobs' packages in turn, a1 b1 a2 b2 a3 b3, on four sessions. The first problem of a2
        // runs until its job is cancelled, and the job is cancelled when that problem starts: a1
        // may have ended by then, a2 is in the middle, a3 waits for its turn.
        using var jobA = new CancellationTokenSource();
        var budget = SmtBudgetRig.Budget(4, 0);
        var a = new[] { Package("a1", 6), Package("a2", 6), Package("a3", 6) };
        var b = new[] { Package("b1", 6), Package("b2", 6), Package("b3", 6) };
        var solver = new SmtScriptedSolver(_ => QUICK_MS);
        solver.Hold(a[1].Id(0));

        // The cancel is done when the session's start is: a3 is still waiting for its turn then,
        // since it comes after a2 and b2 have handed out all of their problems.
        var cancelledAt = TimeSpan.Zero;
        solver.Started += session =>
        {
            if (session.Id != a[1].Id(0))
                return;

            cancelledAt = solver.Elapsed;
            jobA.Cancel();
        };

        var runs = await SolveAllAsync(budget, solver,
            (a[0], jobA.Token), (b[0], CancellationToken.None), (a[1], jobA.Token), (b[1], CancellationToken.None), (a[2], jobA.Token), (b[2], CancellationToken.None));
        var sessions = solver.Sessions;
        foreach (var session in sessions)
            TestContext.Out.WriteLine($"{session.Start.TotalMilliseconds,7:F0} - {session.End?.TotalMilliseconds,7:F0} ms  {session.Id,-6}{(session.Cancelled ? " cancelled" : "")}");

        var a1 = runs[0];
        var a2 = runs[2];
        var a3 = runs[4];

        Assert.Multiple(() =>
        {
            var held = sessions.Single(session => session.Id == a[1].Id(0));
            Assert.That(held.Cancelled, Is.True, "the running session ended at the cancel");
            Assert.That(held.TimedOut, Is.False);
            Assert.That(held.End - TimeSpan.FromTicks(cancelledAt.Ticks), Is.LessThan(TimeSpan.FromSeconds(5)), "soon after it");

            Assert.That(a2.Cancelled, Is.True, "the package in the middle returns nothing");
            Assert.That(a2.Rows, Is.Empty);
            Assert.That(a2.ResultsLeft, Is.False, "and leaves no file of rows");
            Assert.That(sessions.Count(session => session.Ticket == a[1].Reference.Ticket), Is.LessThan(6), "not all of it ran");

            Assert.That(a3.Cancelled, Is.True, "the package that waited for its turn returns nothing");
            Assert.That(sessions.Where(session => session.Ticket == a[2].Reference.Ticket), Is.Empty, "and never started");

            Assert.That(a1.Cancelled || a1.Rows.Count == 6, Is.True, "the first package ended whole or returned nothing");
        });

        if (!a1.Cancelled)
            AssertOwnRows(a1, a[0]);

        for (var index = 0; index < b.Length; index++)
        {
            AssertOwnRows(runs[2 * index + 1], b[index]);
            Assert.That(runs[2 * index + 1].Rows.Select(row => row.Status), Has.All.EqualTo(SmtResultWords.OK), $"{b[index].Name}: the other job's sessions were not cancelled");
        }

        AssertIdle(budget);
    }

    [Test]
    [CancelAfter(120_000)]
    public async Task APackageCancelledBeforeItsTurnReturnsNothingTest()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var budget = SmtBudgetRig.Budget(2, 0);
        var solver = new SmtScriptedSolver(_ => QUICK_MS);
        var gone = Package("gone", 3);
        var kept = Package("kept", 3);

        var runs = await SolveAllAsync(budget, solver, (gone, cancelled.Token), (kept, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(runs[0].Cancelled, Is.True);
            Assert.That(solver.Sessions.Where(session => session.Ticket == gone.Reference.Ticket), Is.Empty);
        });

        AssertOwnRows(runs[1], kept);
        AssertIdle(budget);
    }

    #endregion

    #region Failure Tests

    [Test]
    [CancelAfter(120_000)]
    public async Task APackageWhoseBlobEndsEarlyFailsAloneTest()
    {
        // A package whose reference names one problem more than its file holds: its blob changed
        // after its check. Its last problem is not there to hand out, and it fails with the code of
        // that; the packages beside it end whole.
        var budget = SmtBudgetRig.Budget(3, 0);
        var solver = new SmtScriptedSolver(_ => QUICK_MS);
        var before = Package("before", 5);
        var broken = Package("broken", 4);
        broken.Reference.ProblemCount = 5;
        var after = Package("after", 5);

        var runs = await SolveAllAsync(budget, solver, before, broken, after);

        Assert.That(runs[1].Fault, Is.EqualTo(SmtNodeFaults.PACKAGE_MISMATCH));
        AssertOwnRows(runs[0], before);
        AssertOwnRows(runs[2], after);
        AssertIdle(budget);
    }

    #endregion
}
