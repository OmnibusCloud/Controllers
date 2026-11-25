using System.Diagnostics;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A solver whose sessions are a script, for the node's budget of sessions
/// (<see cref="SmtBudgetRig"/>): a problem takes the milliseconds the script gives its id, or runs
/// until the test lets it go (<see cref="Hold"/>), and ends early when its package is cancelled, as
/// a real session ends at the solver's next read. It counts the sessions running at once and the
/// memory they claim, in all and by session size, and keeps every session's start and end, so a
/// test sees how the budget shared its sessions out.
/// </summary>
internal sealed class SmtScriptedSolver
{
    #region Constants

    /// <summary>The longest a held session waits to be let go: a test that never lets it go fails, it does not hang.</summary>
    private static readonly TimeSpan HOLD_LIMIT = TimeSpan.FromSeconds(60);

    #endregion

    #region Events

    /// <summary>A session started; raised on its thread, outside the solver's lock.</summary>
    public event Action<SmtScriptedSession>? Started;

    #endregion

    #region Fields

    private readonly object m_gate = new();

    private readonly Func<string, int> m_milliseconds;

    private readonly Dictionary<string, ManualResetEventSlim> m_holds = new(StringComparer.Ordinal);

    private readonly List<SmtScriptedSession> m_sessions = new();

    private readonly Dictionary<long, (int Now, int Peak)> m_bySize = new();

    private readonly Stopwatch m_clock = Stopwatch.StartNew();

    private int m_running;

    private long m_runningBytes;

    private int m_peakRunning;

    private long m_peakRunningBytes;

    #endregion

    #region Constructors

    /// <summary>Makes the solver.</summary>
    /// <param name="milliseconds">The milliseconds a problem takes, by its id.</param>
    public SmtScriptedSolver(Func<string, int> milliseconds)
    {
        m_milliseconds = milliseconds;
    }

    #endregion

    #region Functions

    /// <summary>The problem of an id runs until <see cref="Release"/> is called for it, or its package is cancelled.</summary>
    /// <param name="id">The problem's id.</param>
    public void Hold(string id)
    {
        lock (m_gate)
            m_holds[id] = new ManualResetEventSlim(false);
    }

    /// <summary>Lets a held problem end.</summary>
    /// <param name="id">The problem's id.</param>
    public void Release(string id)
    {
        lock (m_gate)
        {
            if (m_holds.TryGetValue(id, out var hold))
                hold.Set();
        }
    }

    /// <summary>
    /// Runs one session: takes its time, or waits to be let go, or ends at the cancel.
    /// </summary>
    /// <param name="ticket">The ticket of the problem's package.</param>
    /// <param name="id">The problem's id.</param>
    /// <param name="sessionBytes">The memory the package counts for a session.</param>
    /// <param name="cancellationToken">The package's token.</param>
    /// <returns>The milliseconds the script gave it (the fuel a row of it states), and whether the cancel ended it.</returns>
    public (int Milliseconds, bool Cancelled) Session(string ticket, string id, long sessionBytes, CancellationToken cancellationToken)
    {
        SmtScriptedSession session;
        ManualResetEventSlim? hold;
        lock (m_gate)
        {
            m_running++;
            m_runningBytes += sessionBytes;
            m_peakRunning = Math.Max(m_peakRunning, m_running);
            m_peakRunningBytes = Math.Max(m_peakRunningBytes, m_runningBytes);

            var (now, peak) = m_bySize.GetValueOrDefault(sessionBytes);
            m_bySize[sessionBytes] = (now + 1, Math.Max(peak, now + 1));

            session = new SmtScriptedSession { Ticket = ticket, Id = id, SessionBytes = sessionBytes, Start = m_clock.Elapsed, RunningAtStart = m_running };
            m_sessions.Add(session);
            m_holds.TryGetValue(id, out hold);
        }

        Started?.Invoke(session);

        var milliseconds = m_milliseconds(id);
        bool cancelled;
        var timedOut = false;
        if (hold != null)
        {
            var woke = WaitHandle.WaitAny([hold.WaitHandle, cancellationToken.WaitHandle], HOLD_LIMIT);
            cancelled = woke == 1;
            timedOut = woke == WaitHandle.WaitTimeout;
        }
        else
        {
            cancelled = cancellationToken.WaitHandle.WaitOne(milliseconds);
        }

        lock (m_gate)
        {
            m_running--;
            m_runningBytes -= sessionBytes;
            var (now, peak) = m_bySize[sessionBytes];
            m_bySize[sessionBytes] = (now - 1, peak);

            session.End = m_clock.Elapsed;
            session.Cancelled = cancelled;
            session.TimedOut = timedOut;
        }

        return (milliseconds, cancelled);
    }

    /// <summary>The most sessions of one size that ran at once.</summary>
    /// <param name="sessionBytes">The size.</param>
    /// <returns>The number; 0 when none of the size ran.</returns>
    public int PeakOf(long sessionBytes)
    {
        lock (m_gate)
            return m_bySize.GetValueOrDefault(sessionBytes).Peak;
    }

    #endregion

    #region Properties

    /// <summary>Every session so far, in the order they started.</summary>
    public IReadOnlyList<SmtScriptedSession> Sessions
    {
        get
        {
            lock (m_gate)
                return m_sessions.ToList();
        }
    }

    /// <summary>The sessions running now.</summary>
    public int Running
    {
        get
        {
            lock (m_gate)
                return m_running;
        }
    }

    /// <summary>The most sessions that ran at once.</summary>
    public int PeakRunning
    {
        get
        {
            lock (m_gate)
                return m_peakRunning;
        }
    }

    /// <summary>The most memory the sessions running at once claimed, bytes.</summary>
    public long PeakRunningBytes
    {
        get
        {
            lock (m_gate)
                return m_peakRunningBytes;
        }
    }

    /// <summary>The time since the solver was made.</summary>
    public TimeSpan Elapsed => m_clock.Elapsed;

    #endregion
}
