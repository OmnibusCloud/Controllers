namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Takes away what a shard leaves on a node's disk without the shard waiting for it.
/// <para>
/// Writing a file and deleting it are not the same to a node's file system. A machine that
/// inspects new files (a real-time scanner) starts on a document a moment after it is written and
/// closed, and a delete that arrives while the inspection runs waits for its end: for a PDF whose
/// one stream inflates to a gigabyte that was 90 s on a machine where the PDF's own run took 7 s.
/// So nothing a shard's work passes through deletes what a crawl sent: a finished PDF run's
/// folder, and at the end the task's whole work folder, are handed over here and deleted on a
/// thread of their own, one after another. Nobody waits for it, and nothing is lost with it: what
/// a deletion leaves goes with the task's scope, and the host sweeps its temp folder when it
/// starts.
/// </para>
/// </summary>
internal static class ShardCleanup
{
    #region Constants

    private const string THREAD_NAME = "corpus-cleanup";

    /// <summary>How long the thread stays with nothing to delete before it ends (the next deletion starts another).</summary>
    private static readonly TimeSpan IDLE = TimeSpan.FromSeconds(30);

    #endregion

    #region Fields

    private static readonly object GATE = new();

    private static readonly Queue<Action> QUEUE = new();

    private static Thread? m_worker;

    /// <summary>Deletions handed over and not finished yet.</summary>
    private static int m_pending;

    #endregion

    #region Functions

    /// <summary>Hands over a folder: it is deleted with everything in it, later, and what cannot be deleted stays for the task's scope.</summary>
    /// <param name="directory">The folder; it need not exist.</param>
    public static void Schedule(string directory)
    {
        Schedule(() => DeleteDirectory(directory));
    }

    /// <summary>Hands over a deletion. It runs once, after the ones handed over before it; what it throws is dropped.</summary>
    /// <param name="deletion">The deletion.</param>
    public static void Schedule(Action deletion)
    {
        lock (GATE)
        {
            QUEUE.Enqueue(deletion);
            m_pending++;

            if (m_worker != null)
            {
                Monitor.PulseAll(GATE);
                return;
            }

            try
            {
                m_worker = new Thread(Work) { IsBackground = true, Name = THREAD_NAME, Priority = ThreadPriority.BelowNormal };
                m_worker.Start();
            }
            catch (Exception exception) when (exception is OutOfMemoryException or ThreadStartException)
            {
                // No thread now: the deletion stays queued for the next one that is handed over.
                m_worker = null;
            }
        }
    }

    /// <summary>Waits until everything handed over so far is done (tests; a tool about to exit).</summary>
    /// <param name="most">The longest wait.</param>
    /// <returns>False when deletions were still pending after the wait.</returns>
    public static bool Drain(TimeSpan most)
    {
        var deadline = DateTime.UtcNow + most;
        lock (GATE)
        {
            while (m_pending > 0)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero)
                    return false;

                Monitor.Wait(GATE, left);
            }

            return true;
        }
    }

    private static void Work()
    {
        while (true)
        {
            Action deletion;
            lock (GATE)
            {
                while (QUEUE.Count == 0)
                {
                    if (Monitor.Wait(GATE, IDLE) || QUEUE.Count > 0)
                        continue;

                    m_worker = null;
                    return;
                }

                deletion = QUEUE.Dequeue();
            }

            try
            {
                deletion();
            }
            catch (Exception)
            {
                // Best effort: what stays goes with the task's scope, or with the host's sweep.
            }

            lock (GATE)
            {
                m_pending--;
                Monitor.PulseAll(GATE);
            }
        }
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    #endregion

    #region Properties

    /// <summary>Deletions handed over and not finished yet.</summary>
    public static int Pending
    {
        get
        {
            lock (GATE)
            {
                return m_pending;
            }
        }
    }

    #endregion
}
