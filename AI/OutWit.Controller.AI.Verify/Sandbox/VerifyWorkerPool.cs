using System.Runtime.ExceptionServices;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Runs a batch's tasks on dedicated worker threads below normal priority: a node is often
/// somebody's desktop, and a sandbox batch would otherwise take every core at normal priority
/// for its whole wall time. Dedicated threads rather than the thread pool, so the priority is
/// ours to set and the degree is exact. Windows honours thread priorities; Linux ignores them
/// for ordinary threads, where the degree cap is what protects the desktop.
/// </summary>
public static class VerifyWorkerPool
{
    #region Constants

    public const ThreadPriority WORKER_PRIORITY = ThreadPriority.BelowNormal;

    private const string WORKER_NAME = "verify-sandbox";

    #endregion

    #region Functions

    /// <summary>
    /// Runs <paramref name="body"/> for every index in <c>[0, count)</c> on at most
    /// <paramref name="degree"/> worker threads and returns when all have finished.
    /// </summary>
    /// <param name="count">Number of work items.</param>
    /// <param name="degree">Upper bound on concurrent workers; clamped to <c>[1, count]</c>.</param>
    /// <param name="body">The work for one index; may run concurrently with itself.</param>
    /// <param name="completed">Called after each index finishes, on the worker that ran it.</param>
    /// <exception cref="Exception">The first exception a worker threw, rethrown after every worker stopped.</exception>
    public static void Run(int count, int degree, Action<int> body, Action<int>? completed = null)
    {
        if (count <= 0)
            return;

        var workers = Math.Max(1, Math.Min(degree, count));
        var next = -1;
        Exception? failure = null;

        var threads = new Thread[workers];
        for (var worker = 0; worker < workers; worker++)
        {
            threads[worker] = new Thread(() =>
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= count || Volatile.Read(ref failure) != null)
                        return;

                    try
                    {
                        body(index);
                        completed?.Invoke(index);
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(ref failure, exception, null);
                        return;
                    }
                }
            })
            {
                IsBackground = true,
                Name = $"{WORKER_NAME}-{worker}",
                Priority = WORKER_PRIORITY
            };
            threads[worker].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    #endregion
}
