using System.Diagnostics;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// The high-water marks of a run's memory: a thread of its own reads the managed heap and the
/// process's working set every few milliseconds until it is stopped. The heap is read as it
/// stands, garbage not yet collected included, so the mark is an upper bound of what was live.
/// A mark splits the run in two: what was seen before a condition first held is kept apart.
/// </summary>
internal sealed class VerifyMemoryMeter : IDisposable
{
    #region Fields

    private readonly Thread m_thread;
    private readonly Func<bool> m_marked;
    private readonly int m_everyMs;
    private volatile bool m_stop;

    #endregion

    #region Constructors

    /// <summary>Starts the meter.</summary>
    /// <param name="everyMs">The time between two readings.</param>
    /// <param name="marked">Says whether the mark was reached: readings before it count for the first part of the run too.</param>
    public VerifyMemoryMeter(int everyMs, Func<bool> marked)
    {
        m_everyMs = everyMs;
        m_marked = marked;
        m_thread = new Thread(Sample) { IsBackground = true, Name = "verify-memory-meter", Priority = ThreadPriority.AboveNormal };
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
            var heap = GC.GetTotalMemory(forceFullCollection: false);
            PeakHeap = Math.Max(PeakHeap, heap);
            if (!m_marked())
                PeakHeapBeforeMark = Math.Max(PeakHeapBeforeMark, heap);

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

    /// <summary>The largest managed heap read during the run.</summary>
    public long PeakHeap { get; private set; }

    /// <summary>The largest managed heap read before the mark.</summary>
    public long PeakHeapBeforeMark { get; private set; }

    /// <summary>The largest working set read during the run.</summary>
    public long PeakWorkingSet { get; private set; }

    /// <summary>How many readings were taken.</summary>
    public int Samples { get; private set; }

    #endregion
}
