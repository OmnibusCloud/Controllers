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

    /// <summary>Priority of the worker threads: below normal, so a desktop stays usable.</summary>
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
    /// <param name="cancellationToken">Once cancelled, no further index starts; the indices already running finish, then the call throws.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled before every index ran.</exception>
    /// <exception cref="Exception">The first exception a worker threw, rethrown after every worker stopped.</exception>
    public static void Run(int count, int degree, Action<int> body, Action<int>? completed = null, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            return;

        var workers = Math.Max(1, Math.Min(degree, count));
        var next = -1;
        Exception? failure = null;

        var threads = new Thread[workers];
        for (var worker = 0; worker < workers; worker++)
        {
            // A worker runs guests: its stack holds the engine's share (VerifyGuestThread).
            var thread = VerifyGuestThread.Create(() =>
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= count || Volatile.Read(ref failure) != null || cancellationToken.IsCancellationRequested)
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
            });
            thread.IsBackground = true;
            thread.Name = $"{WORKER_NAME}-{worker}";
            thread.Priority = WORKER_PRIORITY;
            threads[worker] = thread;
            thread.Start();
        }

        foreach (var thread in threads)
            thread.Join();

        if (failure != null && failure is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failure).Throw();

        // Cancelled (by the token, or by a body that observed it): the caller's token says so.
        cancellationToken.ThrowIfCancellationRequested();

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    #endregion
}
