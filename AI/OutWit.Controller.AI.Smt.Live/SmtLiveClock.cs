using System.Diagnostics;

namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// The clock a measured command reads its times from: the time since the tool's process started,
/// which is the moment the files were ready (the operator starts the tool when the set is on
/// disk), the runtime's own start-up included. The time before the tool's first line is read once
/// from the operating system's start time of the process; everything after it from a stopwatch.
/// </summary>
internal sealed class SmtLiveClock
{
    #region Fields

    private readonly Stopwatch m_watch = Stopwatch.StartNew();
    private readonly TimeSpan m_before;

    #endregion

    #region Constructors

    private SmtLiveClock(TimeSpan before, DateTime startedUtc)
    {
        m_before = before;
        StartedUtc = startedUtc;
    }

    #endregion

    #region Functions

    /// <summary>A clock that starts at the start of this process.</summary>
    /// <returns>The clock.</returns>
    public static SmtLiveClock FromProcessStart()
    {
        var now = DateTime.UtcNow;
        try
        {
            using var process = Process.GetCurrentProcess();
            var started = process.StartTime.ToUniversalTime();
            var before = now - started;
            return before >= TimeSpan.Zero && before < TimeSpan.FromHours(1) ? new SmtLiveClock(before, started) : new SmtLiveClock(TimeSpan.Zero, now);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // A process that may not read its own start time counts from here.
            return new SmtLiveClock(TimeSpan.Zero, now);
        }
    }

    /// <summary>A clock that starts now (a command called from a test).</summary>
    /// <returns>The clock.</returns>
    public static SmtLiveClock StartNow()
    {
        return new SmtLiveClock(TimeSpan.Zero, DateTime.UtcNow);
    }

    #endregion

    #region Properties

    /// <summary>The time since the clock's start.</summary>
    public TimeSpan Elapsed => m_before + m_watch.Elapsed;

    /// <summary>When the clock started, UTC.</summary>
    public DateTime StartedUtc { get; }

    #endregion
}
