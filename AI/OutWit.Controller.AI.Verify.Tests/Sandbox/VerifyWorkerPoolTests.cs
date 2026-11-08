using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The dedicated worker pool a batch runs on: exact degree, every index once, below-normal
/// priority, failures surfaced after the workers stop.
/// </summary>
[TestFixture]
public sealed class VerifyWorkerPoolTests
{
    #region Scheduling Tests

    [Test]
    public void EveryIndexRunsExactlyOnceTest()
    {
        var hits = new int[100];

        VerifyWorkerPool.Run(hits.Length, 4, index => Interlocked.Increment(ref hits[index]));

        Assert.That(hits, Is.All.EqualTo(1));
    }

    [Test]
    public void DegreeBoundsConcurrencyTest()
    {
        var running = 0;
        var peak = 0;

        VerifyWorkerPool.Run(40, 3, _ =>
        {
            var now = Interlocked.Increment(ref running);
            int seen;
            do
            {
                seen = Volatile.Read(ref peak);
            } while (now > seen && Interlocked.CompareExchange(ref peak, now, seen) != seen);

            Thread.Sleep(5);
            Interlocked.Decrement(ref running);
        });

        Assert.That(peak, Is.InRange(1, 3));
    }

    [Test]
    public void CompletedCallbackFiresPerIndexTest()
    {
        var completed = new List<int>();

        VerifyWorkerPool.Run(10, 2, _ => { }, index =>
        {
            lock (completed)
                completed.Add(index);
        });

        Assert.That(completed.OrderBy(index => index), Is.EqualTo(Enumerable.Range(0, 10)));
    }

    [Test]
    public void ZeroCountReturnsWithoutWorkTest()
    {
        var calls = 0;

        VerifyWorkerPool.Run(0, 4, _ => Interlocked.Increment(ref calls));

        Assert.That(calls, Is.Zero);
    }

    #endregion

    #region Priority Tests

    [Test]
    public void WorkersRunBelowNormalOnNamedThreadsTest()
    {
        var priorities = new List<ThreadPriority>();
        var names = new List<string?>();

        VerifyWorkerPool.Run(6, 2, _ =>
        {
            lock (priorities)
            {
                priorities.Add(Thread.CurrentThread.Priority);
                names.Add(Thread.CurrentThread.Name);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(priorities, Is.All.EqualTo(VerifyWorkerPool.WORKER_PRIORITY));
            Assert.That(names, Is.All.Not.Null.And.All.StartWith("verify-sandbox-"));
        });
    }

    #endregion

    #region Failure Tests

    [Test]
    public void FirstFailureSurfacesAfterWorkersStopTest()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            VerifyWorkerPool.Run(20, 4, index =>
            {
                if (index == 7)
                    throw new InvalidOperationException("boom 7");
            }));

        Assert.That(exception!.Message, Is.EqualTo("boom 7"));
    }

    [Test]
    public void CancellationStopsNewIndicesAndThrowsTest()
    {
        using var cts = new CancellationTokenSource();
        var started = new List<int>();
        var gate = new object();

        Assert.Throws<OperationCanceledException>(() =>
            VerifyWorkerPool.Run(100, 2, index =>
            {
                lock (gate)
                    started.Add(index);
                if (index == 3)
                    cts.Cancel();
                Thread.Sleep(5);
            }, cancellationToken: cts.Token));

        Assert.That(started, Has.Count.LessThan(100), "no index starts after the cancel");
        Assert.That(started, Does.Contain(3));
    }

    [Test]
    public void PreCancelledTokenRunsNothingTest()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ran = 0;

        Assert.Throws<OperationCanceledException>(() =>
            VerifyWorkerPool.Run(10, 2, _ => Interlocked.Increment(ref ran), cancellationToken: cts.Token));

        Assert.That(ran, Is.Zero);
    }

    #endregion
}
