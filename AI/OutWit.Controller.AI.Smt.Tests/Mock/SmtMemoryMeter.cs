using System.Diagnostics;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The high-water marks of a run's memory, read by a thread of its own until it is stopped.
/// Two readings of the managed heap, because they answer two questions. Every few milliseconds
/// the heap is read as it stands, garbage not yet collected included: an upper bound of what
/// the run ever had, and as much a property of the collector's mood on the machine as of the
/// code. Every few hundred milliseconds a full collection is asked for first, and what is left
/// is what the run really holds: the number a bound can be put on. The process's working set
/// is read with the first.
/// <para>
/// A mark splits the run in two: what was read before a condition first held is kept apart.
/// </para>
/// </summary>
internal sealed class SmtMemoryMeter : IDisposable
{
    #region Fields

    private readonly Thread m_thread;
    private readonly Func<bool> m_marked;
    private readonly int m_everyMs;
    private readonly int m_liveEvery;
    private volatile bool m_stop;

    #endregion

    #region Constructors

    /// <summary>Starts the meter.</summary>
    /// <param name="everyMs">The time between two readings of the heap as it stands.</param>
    /// <param name="liveEveryMs">The time between two readings of what is live (a full collection each).</param>
    /// <param name="marked">Says whether the mark was reached: readings before it count for the first part of the run too.</param>
    public SmtMemoryMeter(int everyMs, int liveEveryMs, Func<bool> marked)
    {
        m_everyMs = everyMs;
        m_liveEvery = Math.Max(1, liveEveryMs / Math.Max(1, everyMs));
        m_marked = marked;
        m_thread = new Thread(Sample) { IsBackground = true, Name = "smt-memory-meter", Priority = ThreadPriority.AboveNormal };
        m_thread.Start();
    }

    #endregion

    #region Functions

    /// <summary>Stops the meter and waits for its last reading.</summary>
    public void Stop()
    {
        m_stop = true;
        m_thread.Join();
    }

    private void Sample()
    {
        using var process = Process.GetCurrentProcess();
        while (!m_stop)
        {
            var beforeMark = !m_marked();

            var heap = GC.GetTotalMemory(forceFullCollection: false);
            PeakHeap = Math.Max(PeakHeap, heap);
            if (beforeMark)
                PeakHeapBeforeMark = Math.Max(PeakHeapBeforeMark, heap);

            if (Samples % m_liveEvery == 0)
            {
                var live = GC.GetTotalMemory(forceFullCollection: true);
                PeakLive = Math.Max(PeakLive, live);
                if (beforeMark)
                    PeakLiveBeforeMark = Math.Max(PeakLiveBeforeMark, live);

                LiveSamples++;
            }

            process.Refresh();
            PeakWorkingSet = Math.Max(PeakWorkingSet, process.WorkingSet64);
            Samples++;

            Thread.Sleep(m_everyMs);
        }
    }

    #endregion

    #region IDisposable

    /// <summary>Stops the meter.</summary>
    public void Dispose()
    {
        Stop();
    }

    #endregion

    #region Properties

    /// <summary>The largest managed heap read as it stood, garbage included.</summary>
    public long PeakHeap { get; private set; }

    /// <summary>The largest managed heap read as it stood before the mark.</summary>
    public long PeakHeapBeforeMark { get; private set; }

    /// <summary>The most that was live on the managed heap after a full collection.</summary>
    public long PeakLive { get; private set; }

    /// <summary>The most that was live on the managed heap after a full collection, before the mark.</summary>
    public long PeakLiveBeforeMark { get; private set; }

    /// <summary>The largest working set read during the run.</summary>
    public long PeakWorkingSet { get; private set; }

    /// <summary>How many readings were taken.</summary>
    public int Samples { get; private set; }

    /// <summary>How many of them were of what is live.</summary>
    public int LiveSamples { get; private set; }

    #endregion
}
