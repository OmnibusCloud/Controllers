using System.Collections.Concurrent;
using System.Diagnostics;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// Deletes the files and folders a run leaves, off the run's own path. On a machine with a
/// real-time file scanner, deleting a file that was just written can wait on the scanner for a
/// long time (tens of seconds have been seen); on a run's path that wait is paid by every run.
/// A path handed over here is deleted by one background thread below normal priority, which
/// leaves when it has nothing to do (a controller is a plug-in: nothing of it keeps running
/// while it is idle). Deleting is best-effort, as it was: what is left goes with the folder it
/// is in. The backlog is bounded: past it the caller deletes on its own path after all, so what
/// waits on the disk stays bounded too.
/// </summary>
internal static class WasmHostDiscard
{
    #region Constants

    /// <summary>The most paths that may wait; past that a path is deleted by its caller.</summary>
    public const int MAX_PENDING = 4096;

    private const int IDLE_MS = 2_000;
    private const int DRAIN_POLL_MS = 5;

    #endregion

    #region Fields

    private static readonly ConcurrentQueue<string> s_paths = new();
    private static readonly SemaphoreSlim s_signal = new(0);

    private static int s_pending;
    private static int s_running;

    #endregion

    #region Functions

    /// <summary>
    /// Hands a file or a folder over to be deleted in the background.
    /// </summary>
    /// <param name="path">The file or the folder (with everything in it); one that is not there is fine.</param>
    public static void Path(string path)
    {
        if (Interlocked.Increment(ref s_pending) > MAX_PENDING)
        {
            Delete(path);
            Interlocked.Decrement(ref s_pending);
            return;
        }

        s_paths.Enqueue(path);
        s_signal.Release();
        EnsureWorker();
    }

    /// <summary>
    /// Waits until everything handed over so far is deleted (or was tried): for a host that is
    /// closing its folder, and for a test that looks into one.
    /// </summary>
    /// <param name="timeout">How long to wait at the most.</param>
    /// <returns>True when nothing is waiting any more.</returns>
    public static bool Drain(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (Volatile.Read(ref s_pending) > 0)
        {
            if (clock.Elapsed > timeout)
                return false;

            Thread.Sleep(DRAIN_POLL_MS);
        }

        return true;
    }

    private static void EnsureWorker()
    {
        if (Interlocked.CompareExchange(ref s_running, 1, 0) != 0)
            return;

        var worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "wasm-discard",
            Priority = ThreadPriority.BelowNormal
        };
        worker.Start();
    }

    private static void Work()
    {
        while (s_signal.Wait(IDLE_MS))
        {
            if (!s_paths.TryDequeue(out var path))
                continue;

            Delete(path);
            Interlocked.Decrement(ref s_pending);
        }

        // Nothing for a while: the thread leaves. A path that came while it was leaving starts another.
        Volatile.Write(ref s_running, 0);
        if (!s_paths.IsEmpty)
            EnsureWorker();
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // Best-effort, whatever it was: what is left goes with the folder it is in, and
            // nothing that fails here may end the thread, or the process with it.
        }
    }

    #endregion
}
