using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Sandbox;

/// <summary>
/// The node's budget of sessions shares its sessions across the packages of a share: while a
/// package's slowest problem runs, the next package's problems take the sessions it does not need;
/// never more sessions run at once than the degree; a share of long-tail packages ends near the sum
/// of its work over the degree instead of the sum of its packages' slowest problems; the machine is
/// read when no other package is open. Every package returns exactly its own rows. The solver is a
/// script (<see cref="SmtScriptedSolver"/>): what is tested is how the sessions are shared, not
/// what a session says.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtSessionBudgetTests : SmtSessionBudgetTestBase
{
    #region Constants

    private const int FAST_MS = 40;

    #endregion

    #region Sharing Tests

    [Test]
    [CancelAfter(120_000)]
    public async Task TheNextPackagesProblemsStartWhileAPackagesSlowestRunsTest()
    {
        // Four sessions; three packages of six problems, the first problem of the first two held
        // until a problem of the package after it has started. A node that solved one package at a
        // time would never start the next package while the held problem runs, and the held
        // problem would end by its limit, not by the test.
        var budget = SmtBudgetRig.Budget(4, 0);
        var packages = new[] { Package("first", 6), Package("second", 6), Package("third", 6) };
        var solver = new SmtScriptedSolver(_ => FAST_MS);
        solver.Hold(packages[0].Id(0));
        solver.Hold(packages[1].Id(0));
        solver.Started += session =>
        {
            if (session.Ticket == packages[1].Reference.Ticket)
                solver.Release(packages[0].Id(0));
            else if (session.Ticket == packages[2].Reference.Ticket)
                solver.Release(packages[1].Id(0));
        };

        var runs = await SolveAllAsync(budget, solver, packages);
        var sessions = solver.Sessions;
        foreach (var session in sessions)
            TestContext.Out.WriteLine($"{session.Start.TotalMilliseconds,7:F0} - {session.End?.TotalMilliseconds,7:F0} ms  {session.Id,-10} with {session.RunningAtStart} running");

        Assert.Multiple(() =>
        {
            Assert.That(sessions.Where(session => session.TimedOut).Select(session => session.Id), Is.Empty, "every held problem was let go by the next package's start");

            var firstSlow = sessions.Single(session => session.Id == packages[0].Id(0));
            var secondStart = sessions.Where(session => session.Ticket == packages[1].Reference.Ticket).Min(session => session.Start);
            Assert.That(secondStart, Is.LessThan(firstSlow.End ?? TimeSpan.MaxValue), "the second package started while the first one's slowest problem ran");

            Assert.That(solver.PeakRunning, Is.EqualTo(4), "the four sessions were used, and never more");
            Assert.That(SmtBudgetRig.Counts(budget).PeakRunning, Is.EqualTo(4));
        });

        for (var index = 0; index < packages.Length; index++)
            AssertOwnRows(runs[index], packages[index]);

        AssertIdle(budget);
    }

    [Test]
    [CancelAfter(120_000)]
    public async Task NeverMoreSessionsAtOnceThanTheDegreeTest()
    {
        // Three sessions, eight packages of five problems of 5 to 40 ms: many packages, short
        // problems, sessions changing hands all the time.
        var random = new Random(20261125);
        var times = new Dictionary<string, int>();
        var packages = Enumerable.Range(0, 8).Select(index => Package($"p{index}", 5)).ToArray();
        foreach (var problem in packages.SelectMany(package => package.Problems))
            times[problem.Id!] = random.Next(5, 41);

        var budget = SmtBudgetRig.Budget(3, 0);
        var solver = new SmtScriptedSolver(id => times[id]);

        var runs = await SolveAllAsync(budget, solver, packages);
        var counts = SmtBudgetRig.Counts(budget);
        TestContext.Out.WriteLine($"{solver.Sessions.Count} sessions, at most {solver.PeakRunning} at once; at most {counts.PeakOpen} package(s) open; {solver.Elapsed.TotalMilliseconds:F0} ms");

        Assert.Multiple(() =>
        {
            Assert.That(solver.PeakRunning, Is.EqualTo(3), "the degree, reached and never passed");
            Assert.That(counts.PeakRunning, Is.EqualTo(3));
            Assert.That(solver.Sessions.Max(session => session.RunningAtStart), Is.LessThanOrEqualTo(3));
            Assert.That(counts.PeakOpen, Is.LessThanOrEqualTo(3 + 1), "no more packages open than the sessions and one");
            Assert.That(counts.PeakOpen, Is.LessThanOrEqualTo(SmtBudgetRig.MaxOpenPackages));
            Assert.That(solver.Sessions, Has.Count.EqualTo(40), "every problem ran once");
        });

        for (var index = 0; index < packages.Length; index++)
            AssertOwnRows(runs[index], packages[index]);

        AssertIdle(budget);
    }

    [Test]
    [CancelAfter(120_000)]
    public async Task ALongTailEndsNearItsWorkOverTheDegreeTest()
    {
        // Four sessions; six packages of eight problems, one of 1.2 s and seven of 0.15 s each.
        // One package at a time (the budget let one package open at once, as a node of 0.4.0 ran a
        // share) ends near the sum of the slowest problems, 7.2 s; the shared budget near the work
        // over the degree, 3.4 s.
        const int SESSIONS = 4;
        const int SLOW_MS = 1_200;
        const int QUICK_MS = 150;
        const int PACKAGES = 6;
        const int PROBLEMS = 8;

        var ideal = TimeSpan.FromMilliseconds(PACKAGES * (SLOW_MS + (PROBLEMS - 1) * QUICK_MS) / (double)SESSIONS);
        var sumOfSlowest = TimeSpan.FromMilliseconds(PACKAGES * SLOW_MS);

        var shared = await LongTailAsync(SmtBudgetRig.Budget(SESSIONS, 0), PACKAGES, PROBLEMS, SLOW_MS, QUICK_MS);
        var oneAtATime = await LongTailAsync(SmtBudgetRig.Budget(SESSIONS, 0, maxOpen: 1), PACKAGES, PROBLEMS, SLOW_MS, QUICK_MS);

        TestContext.Out.WriteLine($"work over the degree {ideal.TotalSeconds:F2} s; the shared budget {shared.TotalSeconds:F2} s; " +
                                  $"one package at a time {oneAtATime.TotalSeconds:F2} s (the sum of the slowest problems {sumOfSlowest.TotalSeconds:F2} s)");

        Assert.Multiple(() =>
        {
            Assert.That(shared, Is.GreaterThanOrEqualTo(ideal * 0.95), "no faster than the work allows");
            Assert.That(shared, Is.LessThan(ideal * 1.4), "near the work over the degree");
            Assert.That(oneAtATime, Is.GreaterThanOrEqualTo(sumOfSlowest * 0.95), "one package at a time waits for each slowest problem");
            Assert.That(shared, Is.LessThan(oneAtATime * 0.7), "the shared budget does not");
        });
    }

    #endregion

    #region Reading Tests

    [Test]
    [CancelAfter(120_000)]
    public async Task TheMachineIsReadWhenNoOtherPackageIsOpenTest()
    {
        // Four packages started together are one stretch of work: the machine is read when the
        // first is let in, and the others take that reading, since what the machine has available
        // while sessions run counts them. A package after the stretch reads it again.
        var reads = 0;
        var budget = SmtBudgetRig.Budget(() =>
        {
            Interlocked.Increment(ref reads);
            return (4, 64 * GIGABYTE);
        });
        var solver = new SmtScriptedSolver(_ => FAST_MS);

        var share = Enumerable.Range(0, 4).Select(index => Package($"share{index}", 6)).ToArray();
        var runs = await SolveAllAsync(budget, solver, share);
        var readsInShare = Volatile.Read(ref reads);

        var later = Package("later", 3);
        var laterRun = (await SolveAllAsync(budget, solver, later))[0];

        Assert.Multiple(() =>
        {
            Assert.That(readsInShare, Is.EqualTo(1), "one reading for the share");
            Assert.That(Volatile.Read(ref reads), Is.EqualTo(2), "a package after it reads again");
            Assert.That(SmtBudgetRig.Counts(budget).Reads, Is.EqualTo(2));
        });

        for (var index = 0; index < share.Length; index++)
            AssertOwnRows(runs[index], share[index]);

        AssertOwnRows(laterRun, later);
        AssertIdle(budget);
    }

    #endregion

    #region Tools

    private async Task<TimeSpan> LongTailAsync(object budget, int packages, int problems, int slowMs, int quickMs)
    {
        var share = Enumerable.Range(0, packages).Select(index => Package($"tail{index}", problems)).ToArray();
        var slow = share.Select(package => package.Id(0)).ToHashSet();
        var solver = new SmtScriptedSolver(id => slow.Contains(id) ? slowMs : quickMs);

        var started = solver.Elapsed;
        var runs = await SolveAllAsync(budget, solver, share);
        var elapsed = solver.Elapsed - started;

        for (var index = 0; index < share.Length; index++)
            AssertOwnRows(runs[index], share[index]);

        AssertIdle(budget);
        return elapsed;
    }

    #endregion
}
