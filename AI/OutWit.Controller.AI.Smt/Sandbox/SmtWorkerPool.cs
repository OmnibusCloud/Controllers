using System.Runtime.ExceptionServices;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// Runs a fixed set of sessions (the benchmark's runs, a local run of smt-live) on dedicated worker
/// threads below normal priority: a node is often somebody's desktop, and the sessions would
/// otherwise take every core at normal priority for their whole wall time. Dedicated threads rather
/// than the thread pool, so that the priority is ours to set and the degree is exact; each is made
/// to hold a guest's stack (<see cref="WasmHostGuestThread"/>). Windows honours thread priorities;
/// Linux ignores them for ordinary threads, where the degree cap is what protects the desktop. The
/// packages of a node share the sessions of its budget instead (<see cref="SmtSessionBudget"/>),
/// on threads of the same kind.
/// </summary>
internal static class SmtWorkerPool
{
    #region Constants

    /// <summary>Priority of the worker threads: below normal, so a desktop stays usable.</summary>
    public const ThreadPriority WORKER_PRIORITY = ThreadPriority.BelowNormal;

    private const string WORKER_NAME = "smt-session";

    #endregion

    #region Functions

    /// <summary>
    /// Runs <paramref name="body"/> for every index in <c>[0, count)</c> on at most
    /// <paramref name="degree"/> worker threads and returns when all have finished.
    /// </summary>
    /// <param name="count">Number of work items.</param>
    /// <param name="degree">Upper bound on concurrent workers; clamped to <c>[1, count]</c>.</param>
    /// <param name="body">The work for one index; may run concurrently with itself.</param>
    /// <param name="cancellationToken">Once cancelled, no further index starts; the indices already running finish, then the call throws.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled before every index ran.</exception>
    /// <exception cref="Exception">The first exception a worker threw, or what kept a worker's thread from starting, rethrown after every worker that was started has stopped.</exception>
    public static void Run(int count, int degree, Action<int> body, CancellationToken cancellationToken = default)
    {
        Run(count, degree, body, WasmHostGuestThread.Create, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="body"/> as <see cref="Run(int, int, Action{int}, CancellationToken)"/> does, on threads made as told.
    /// </summary>
    /// <param name="count">Number of work items.</param>
    /// <param name="degree">Upper bound on concurrent workers; clamped to <c>[1, count]</c>.</param>
    /// <param name="body">The work for one index; may run concurrently with itself.</param>
    /// <param name="makeThread">Makes a worker's thread, not started.</param>
    /// <param name="cancellationToken">Once cancelled, no further index starts; the indices already running finish, then the call throws.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled before every index ran.</exception>
    /// <exception cref="Exception">The first exception a worker threw, or what kept a worker's thread from starting, rethrown after every worker that was started has stopped.</exception>
    public static void Run(int count, int degree, Action<int> body, Func<Action, Thread> makeThread, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            return;

        var workers = Math.Max(1, Math.Min(degree, count));
        var next = -1;
        Exception? failure = null;

        var threads = new List<Thread>(workers);
        for (var worker = 0; worker < workers; worker++)
        {
            try
            {
                var thread = makeThread(() =>
                {
                    while (true)
                    {
                        if (Volatile.Read(ref failure) != null || cancellationToken.IsCancellationRequested)
                            return;

                        var index = Interlocked.Increment(ref next);
                        if (index >= count)
                            return;

                        try
                        {
                            body(index);
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
                thread.Start();
                threads.Add(thread);
            }
            catch (Exception exception)
            {
                // No thread for this worker. The workers already started take no further index
                // and are waited for below, before the caller is told: the caller's own things
                // (a sandbox, a file) are still in use while one of them runs.
                Interlocked.CompareExchange(ref failure, exception, null);
                break;
            }
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
