using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Sandbox;

/// <summary>
/// The memory of the node's budget of sessions: packages of one session size get the degree rule's
/// sessions exactly, as a package had on its own; packages of two sizes (of two jobs) share the
/// bytes the machine gives, a large session in the room of several small ones, never more than the
/// memory holds, each size never more than its own degree; a package of which not one session fits
/// is refused (<c>memory_insufficient</c>) and gives its place up to the next one.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtSessionBudgetMemoryTests : SmtSessionBudgetTestBase
{
    #region Constants

    private const int FAST_MS = 30;

    #endregion

    #region Degree Tests

    [TestCase(16, 64, 1_024)]
    [TestCase(16, 16, 1_024)]
    [TestCase(4, 64, 1_024)]
    [TestCase(8, 24, 4_096)]
    [TestCase(32, 0, 1_024)]
    [TestCase(6, 12, 64)]
    [CancelAfter(120_000)]
    public async Task SessionsOfOneSizeAreTheDegreeRulesTest(int cores, int gigabytes, int memoryMegabytes)
    {
        var options = new SmtOptionsData { MemoryMegabytes = memoryMegabytes };
        var available = gigabytes * GIGABYTE;
        var (degree, reason) = SmtNodeRig.Degree(cores, available, SmtNodeRig.SessionBytes(options));
        var budget = SmtBudgetRig.Budget(cores, available);
        var solver = new SmtScriptedSolver(_ => FAST_MS);

        // Two packages, each with more problems than the degree. The first problems of the first
        // package, as many as the degree, run until that many sessions run at once: the budget
        // reaches the degree by its own doing, not because the problems were slow enough for the
        // threads to meet. A budget that gave fewer sessions would leave them held to their limit.
        var packages = new[] { Package("a", 3 * degree, options), Package("b", 3 * degree, options) };
        var held = Enumerable.Range(0, degree).Select(packages[0].Id).ToList();
        held.ForEach(solver.Hold);
        solver.Started += session =>
        {
            if (session.RunningAtStart >= degree)
                held.ForEach(solver.Release);
        };

        var runs = await SolveAllAsync(budget, solver, packages);

        Assert.Multiple(() =>
        {
            Assert.That(solver.Sessions.Where(session => session.TimedOut).Select(session => session.Id), Is.Empty, "the degree was reached: every held problem was let go");
            Assert.That(runs.Select(run => run.Degree), Has.All.EqualTo(degree), "each package is told the rule's degree");
            Assert.That(runs.Select(run => run.Reason), Has.All.EqualTo(reason));
            Assert.That(solver.PeakRunning, Is.EqualTo(degree), "and the budget runs exactly that many of its sessions at once");
        });

        AssertOwnRows(runs[0], packages[0]);
        AssertOwnRows(runs[1], packages[1]);
        AssertIdle(budget);
    }

    #endregion

    #region Size Tests

    [Test]
    [CancelAfter(120_000)]
    public async Task PackagesOfTwoSessionSizesShareTheMemoryTest()
    {
        // A large session (the memory option's ceiling, 8 GiB and the margin) and a small one (the
        // default, 2,752 MiB). The machine holds two large ones and two small ones beside them; by
        // itself the small size would get its cores, eight.
        var large = new SmtOptionsData { MemoryMegabytes = 4_096 };
        var small = new SmtOptionsData();
        var largeBytes = SmtNodeRig.SessionBytes(large);
        var smallBytes = SmtNodeRig.SessionBytes(small);
        var available = (long)((2 * largeBytes + 2 * smallBytes) / 0.75) + MEGABYTE;
        var largeDegree = SmtNodeRig.Degree(8, available, largeBytes).Degree;
        var smallDegree = SmtNodeRig.Degree(8, available, smallBytes).Degree;

        var budget = SmtBudgetRig.Budget(8, available);
        var capacity = (long)(available * SmtNodeRig.RamHeadroom);

        // Two jobs' packages in turn, large, small, large, small. The last problem of the first
        // large package is held until a small session starts: the small package is let in when the
        // large one has handed out all of its problems, and its sessions run in the room the
        // large ones leave.
        var packages = new[] { Package("large1", 6, large), Package("small1", 10, small), Package("large2", 6, large), Package("small2", 10, small) };
        var solver = new SmtScriptedSolver(id => id.StartsWith("large", StringComparison.Ordinal) ? 4 * FAST_MS : FAST_MS);
        solver.Hold(packages[0].Id(5));
        solver.Started += session =>
        {
            if (session.SessionBytes == smallBytes)
                solver.Release(packages[0].Id(5));
        };

        var runs = await SolveAllAsync(budget, solver, packages);
        var sessions = solver.Sessions;
        TestContext.Out.WriteLine($"large {largeBytes / MEGABYTE} MiB (degree {largeDegree}), small {smallBytes / MEGABYTE} MiB (degree {smallDegree}), room {capacity / MEGABYTE} MiB; " +
                                  $"at most {solver.PeakRunningBytes / MEGABYTE} MiB claimed at once, {solver.PeakOf(largeBytes)} large and {solver.PeakOf(smallBytes)} small at most");

        Assert.Multiple(() =>
        {
            Assert.That(largeDegree, Is.EqualTo(2), "the machine of the test");
            Assert.That(smallDegree, Is.EqualTo(8));
            Assert.That(sessions.Where(session => session.TimedOut).Select(session => session.Id), Is.Empty, "a small session started while the large package's last problem ran");

            Assert.That(solver.PeakRunningBytes, Is.LessThanOrEqualTo(capacity), "never more memory claimed than three quarters of the available");
            Assert.That(SmtBudgetRig.Counts(budget).PeakRunningBytes, Is.LessThanOrEqualTo(capacity));
            Assert.That(solver.PeakOf(largeBytes), Is.EqualTo(largeDegree), "the large sessions at their degree");
            Assert.That(solver.PeakOf(smallBytes), Is.LessThanOrEqualTo(smallDegree));
            Assert.That(solver.PeakRunning, Is.LessThanOrEqualTo(8), "and never more than the cores");

            Assert.That(runs[0].Degree, Is.EqualTo(largeDegree));
            Assert.That(runs[1].Degree, Is.EqualTo(smallDegree));
        });

        for (var index = 0; index < packages.Length; index++)
            AssertOwnRows(runs[index], packages[index]);

        AssertIdle(budget);
    }

    [Test]
    [CancelAfter(120_000)]
    public async Task APackageOfWhichNoSessionFitsGivesItsPlaceUpTest()
    {
        // Three quarters of 3 GiB is 2,304 MiB: a session at the default options (2,752 MiB) does
        // not fit; sessions of a 64 MB ceiling do, several times.
        var budget = SmtBudgetRig.Budget(8, 3 * GIGABYTE);
        var solver = new SmtScriptedSolver(_ => FAST_MS);
        var smallOptions = new SmtOptionsData { MemoryMegabytes = 64 };
        var degree = SmtNodeRig.Degree(8, 3 * GIGABYTE, SmtNodeRig.SessionBytes(smallOptions)).Degree;
        var refused = Package("refused", 4);
        var taken = Package("taken", 2 * degree, smallOptions);

        var runs = await SolveAllAsync(budget, solver, refused, taken);

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.Degree(8, 3 * GIGABYTE, SmtNodeRig.SessionBytes(new SmtOptionsData())).Degree, Is.Zero, "the machine of the test");
            Assert.That(degree, Is.InRange(2, 7));
            Assert.That(runs[0].Fault, Is.EqualTo(SmtNodeFaults.MEMORY_INSUFFICIENT));
            Assert.That(runs[0].Rows, Is.Empty);
            Assert.That(solver.Sessions.Where(session => session.Ticket == refused.Reference.Ticket), Is.Empty, "nothing of it ran");
            Assert.That(runs[1].Degree, Is.EqualTo(degree));
            Assert.That(solver.PeakRunning, Is.EqualTo(degree));
        });

        AssertOwnRows(runs[1], taken);
        AssertIdle(budget);
    }

    [Test]
    [CancelAfter(120_000)]
    public async Task APackageOfWhichNoSessionFitsThatReachesTheBudgetFailsThereTest()
    {
        // The budget's own guard, behind the refusal a package gets when it asks for its degree:
        // a package no session of which fits, handed to the budget all the same, must not wait
        // without end for room that never comes. Three quarters of 3 GiB hold no session at the
        // default options.
        var budget = SmtBudgetRig.Budget(8, 3 * GIGABYTE);
        var solver = new SmtScriptedSolver(_ => FAST_MS);
        var large = Package("large", 4);
        var small = Package("small", 4, new SmtOptionsData { MemoryMegabytes = 64 });

        var failure = await SmtBudgetRig.RunUncheckedAsync(budget, large.Reference, large.ProblemsPath, large.ResultsPath, solver);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.InstanceOf<InvalidOperationException>().With.Message.EqualTo("not one session of the package fits in the budget"));
            Assert.That(solver.Sessions, Is.Empty, "nothing of it ran");
            Assert.That(File.Exists(large.ResultsPath), Is.False, "and it leaves no file of rows");
        });
        AssertIdle(budget);

        var runs = await SolveAllAsync(budget, solver, small);

        AssertOwnRows(runs[0], small);
        AssertIdle(budget);
    }

    #endregion
}
