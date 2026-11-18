using Microsoft.Extensions.Logging;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A host's logger that is not thread safe and knows when it is misused: its lines go into a
/// plain list without a lock, a call stays inside for a moment so that two threads that come
/// together do meet, and a call that finds another one inside counts an overlap and throws, as a
/// list written from two threads does. A caller that takes turns never sees it throw.
/// </summary>
public sealed class CorpusTestLoggerUnguarded : ILogger
{
    #region Fields

    private readonly List<string> m_lines = [];

    private int m_inside;

    private int m_overlaps;

    #endregion

    #region ILogger

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (Interlocked.Increment(ref m_inside) != 1)
        {
            Interlocked.Increment(ref m_overlaps);
            Interlocked.Decrement(ref m_inside);
            throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
        }

        try
        {
            // Long enough for a second thread to arrive while this one is inside.
            Thread.SpinWait(20_000);
            Thread.Yield();
            m_lines.Add(formatter(state, exception));
        }
        finally
        {
            Interlocked.Decrement(ref m_inside);
        }
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    #endregion

    #region Properties

    /// <summary>The lines written, in the order they came.</summary>
    public IReadOnlyList<string> Lines => m_lines;

    /// <summary>How many calls found another call inside.</summary>
    public int Overlaps => Volatile.Read(ref m_overlaps);

    #endregion
}
