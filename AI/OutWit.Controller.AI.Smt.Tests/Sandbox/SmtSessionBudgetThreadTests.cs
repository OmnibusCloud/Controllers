using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Sandbox;

/// <summary>
/// A machine that has no thread to give for a session, whatever the reason. The budget counts a
/// problem as running when it takes it, before the thread of its session is made and started: a
/// thread that cannot be is the failure of that problem's package alone. The counts are put back,
/// the package fails with what the machine said, the packages beside it and after it run as
/// before, and nothing escapes on a session's own thread, where an exception nobody catches ends
/// the process.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SmtSessionBudgetThreadTests : SmtSessionBudgetTestBase
{
    #region Constants

    private const int FAST_MS = 30;

    private const string NO_THREAD = "the test's machine has no thread to give";

    #endregion

    #region Fields

    private readonly ManualResetEventSlim m_parked = new(false);

    private readonly List<Thread> m_spent = [];

    #endregion

    #region Setup

    [SetUp]
    public void SetupThreads()
    {
        m_parked.Reset();
        m_spent.Clear();
    }

    [TearDown]
    public void TearDownThreads()
    {
        m_parked.Set();
        m_spent.ForEach(thread => thread.Join());
    }

    #endregion

    #region Tests

    [TestCase(false, TestName = "{m}(the thread cannot be made)")]
    [TestCase(true, TestName = "{m}(the thread cannot be started)")]
    [CancelAfter(120_000)]
    public async Task APackageWhoseSessionGetsNoThreadFailsAndTheBudgetIsWholeAgainTest(bool made)
    {
        // Two sessions at once; the first thread the budget asks for is not to be had.
        var refused = 0;
        var budget = SmtBudgetRig.Budget(2, 0, body => Interlocked.Increment(ref refused) == 1 ? Unusable(made) : new Thread(() => body()));
        var solver = new SmtScriptedSolver(_ => FAST_MS);
        var failed = Package("failed", 3);
        var after = Package("after", 3);

        var failure = Assert.CatchAsync(async () => await SolveAllAsync(budget, solver, failed));
        var counts = SmtBudgetRig.Counts(budget);

        Assert.Multiple(() =>
        {
            Assert.That(failure, made ? Is.InstanceOf<ThreadStateException>() : Is.InstanceOf<InvalidOperationException>().With.Message.EqualTo(NO_THREAD), "the package fails with what the machine said");
            Assert.That(solver.Sessions, Is.Empty, "the problem was never started, and no further one was handed out");
            Assert.That(File.Exists(failed.ResultsPath), Is.False, "the failed package leaves no file of rows");
            Assert.That(counts.Running, Is.Zero, "the problem that got no thread is not counted as running any more");
            Assert.That(counts.Open, Is.Zero, "and its package has left the budget");
        });
        AssertIdle(budget);

        // The budget is whole: the next package gets both sessions and returns its rows.
        var runs = await SolveAllAsync(budget, solver, after);

        AssertOwnRows(runs[0], after);
        Assert.That(solver.PeakRunning, Is.EqualTo(2), "both sessions are there for the next package");
        AssertIdle(budget);
    }

    [Test]
    [CancelAfter(120_000)]
    public async Task AThreadRefusedOnASessionsOwnThreadFailsItsPackageAndNothingElseTest()
    {
        // A machine whose memory holds one large session and nothing beside it, or three small
        // ones. The large package's one problem runs; the small package has given its three
        // problems to the budget and waits for the room. When the large session ends, its thread
        // takes the first small problem itself and asks for a thread for the second: on a
        // session's own thread, under the budget's lock. That thread is refused.
        var large = new SmtOptionsData();
        var small = new SmtOptionsData { MemoryMegabytes = 64 };
        var largeBytes = SmtNodeRig.SessionBytes(large);
        var smallBytes = SmtNodeRig.SessionBytes(small);
        var available = (long)((largeBytes + smallBytes / 2) / SmtNodeRig.RamHeadroom) + MEGABYTE;

        var asked = 0;
        var budget = SmtBudgetRig.Budget(4, available, body => Interlocked.Increment(ref asked) == 2 ? throw new InvalidOperationException(NO_THREAD) : new Thread(() => body()));
        var solver = new SmtScriptedSolver(_ => FAST_MS);
        var first = Package("large", 1, large);
        var second = Package("small", 3, small);
        var third = Package("later", 3, small);
        solver.Hold(first.Id(0));

        var largeRun = SmtBudgetRig.SolveAsync(budget, first.Reference, first.ProblemsPath, first.ResultsPath, solver, CancellationToken.None);
        var smallRun = SmtBudgetRig.SolveAsync(budget, second.Reference, second.ProblemsPath, second.ResultsPath, solver, CancellationToken.None,
            handedOut: () => solver.Release(first.Id(0)));

        var failure = Assert.CatchAsync(async () => await smallRun);
        var done = await largeRun;
        var sessions = solver.Sessions;

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.Degree(4, available, largeBytes).Degree, Is.EqualTo(1), "the machine of the test: one large session");
            Assert.That(SmtNodeRig.Degree(4, available, smallBytes).Degree, Is.GreaterThanOrEqualTo(3), "or three small ones");
            Assert.That(sessions.Where(session => session.TimedOut).Select(session => session.Id), Is.Empty, "the large problem was let go when the small package had handed its problems out");

            Assert.That(failure, Is.InstanceOf<InvalidOperationException>().With.Message.EqualTo(NO_THREAD), "the small package fails with what the machine said");
            Assert.That(sessions.Where(session => session.Ticket == second.Reference.Ticket).Select(session => session.Id), Is.EqualTo(new[] { second.Id(0) }),
                "the problem the large session's thread had taken ran to its end; the one without a thread never started, and the third was not handed out");
            Assert.That(File.Exists(second.ResultsPath), Is.False, "the failed package leaves no file of rows");
        });

        AssertOwnRows(done, first);
        AssertIdle(budget);

        var runs = await SolveAllAsync(budget, solver, third);

        AssertOwnRows(runs[0], third);
        AssertIdle(budget);
    }

    [Test]
    [CancelAfter(120_000)]
    public void APoolWhoseWorkerGetsNoThreadWaitsForTheWorkersItStartedTest()
    {
        // Three workers; the second thread is not to be had. The first worker is in the middle of
        // its work then, and stays there for a while: the pool's caller must not go on (and take
        // away what the work uses) while a worker still runs.
        var started = new List<Thread>();
        using var working = new ManualResetEventSlim(false);
        var ended = 0;
        var indices = 0;

        var failure = Assert.Catch(() => SmtBudgetRig.Pool(6, 3, _ =>
        {
            Interlocked.Increment(ref indices);
            working.Set();
            Thread.Sleep(10 * FAST_MS);
            Interlocked.Increment(ref ended);
        }, body =>
        {
            if (started.Count == 1)
            {
                working.Wait();
                throw new InvalidOperationException(NO_THREAD);
            }

            var thread = new Thread(() => body());
            started.Add(thread);
            return thread;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.InstanceOf<InvalidOperationException>().With.Message.EqualTo(NO_THREAD), "the pool's caller is told what the machine said");
            Assert.That(started, Has.Count.EqualTo(1));
            Assert.That(started.Select(thread => thread.IsAlive), Has.All.False, "and only when the worker that was started has ended");
            Assert.That(Volatile.Read(ref ended), Is.EqualTo(Volatile.Read(ref indices)), "every index a worker took ran to its end");
            Assert.That(Volatile.Read(ref indices), Is.EqualTo(1), "and no further index was taken after the failure");
        });
    }

    [Test]
    [CancelAfter(120_000)]
    public void APoolWhoseThreadsAllStartRunsEveryIndexOnceTest()
    {
        var runs = new int[20];
        var threads = 0;

        SmtBudgetRig.Pool(runs.Length, 4, index => Interlocked.Increment(ref runs[index]), body =>
        {
            Interlocked.Increment(ref threads);
            return new Thread(() => body());
        });

        Assert.Multiple(() =>
        {
            Assert.That(runs, Has.All.EqualTo(1));
            Assert.That(threads, Is.EqualTo(4), "a thread a worker, at the degree");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// A thread nobody can run a session on: none at all (the machine refuses to make one), or one
    /// that is running already, which takes its name and its priority and then refuses to start.
    /// </summary>
    private Thread Unusable(bool made)
    {
        if (!made)
            throw new InvalidOperationException(NO_THREAD);

        var thread = new Thread(() => m_parked.Wait());
        thread.Start();
        m_spent.Add(thread);
        return thread;
    }

    #endregion
}
