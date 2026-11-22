using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// A host's logger that is not thread safe, the shape cloud clients up to 2.2.5 gave a
/// controller: every line is added to an observable list without a lock. Two threads inside it
/// at once is what broke that list on a node; here a second thread is noticed for certain
/// rather than by luck: it is counted and it throws, as the list did.
/// </summary>
internal sealed class VerifyUnlockedLogger : ILogger
{
    #region Constants

    private const int SPINS_INSIDE = 64;

    #endregion

    #region Fields

    private readonly ObservableCollection<string> m_lines = [];

    private readonly TimeSpan m_inside;

    private int m_busy;

    private int m_overlaps;

    #endregion

    #region Constructors

    /// <summary>A logger that takes a line in a few instructions.</summary>
    public VerifyUnlockedLogger()
        : this(TimeSpan.Zero)
    {
    }

    /// <summary>A logger that takes its time over a line, so that lines written close together are sure to meet.</summary>
    /// <param name="inside">How long a line keeps the logger.</param>
    public VerifyUnlockedLogger(TimeSpan inside)
    {
        m_inside = inside;
    }

    #endregion

    #region ILogger

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (Interlocked.Exchange(ref m_busy, 1) == 1)
        {
            Interlocked.Increment(ref m_overlaps);
            throw new IndexOutOfRangeException("Index was outside the bounds of the array.");
        }

        try
        {
            // Long enough inside for another thread to arrive while this one is here.
            if (m_inside > TimeSpan.Zero)
                Thread.Sleep(m_inside);
            else
                Thread.SpinWait(SPINS_INSIDE);

            m_lines.Add($"{logLevel}: {formatter(state, exception)}");
        }
        finally
        {
            Volatile.Write(ref m_busy, 0);
        }
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    #endregion

    #region Properties

    /// <summary>The lines the logger kept, in the order they came.</summary>
    public IReadOnlyList<string> Lines => m_lines;

    /// <summary>How many times a thread found another one inside.</summary>
    public int Overlaps => Volatile.Read(ref m_overlaps);

    #endregion
}
