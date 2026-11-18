using Microsoft.Extensions.Logging;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A host's logger that fails every call the way the in-memory logger of clients up to 2.2.5
/// failed under parallel tasks: an <see cref="IndexOutOfRangeException"/> out of its list. Counts
/// the calls it received.
/// </summary>
public sealed class CorpusTestLoggerThrowing : ILogger
{
    #region Constants

    /// <summary>The text a node's batch failed with on the fleet: what must never leave a node.</summary>
    public const string MESSAGE = "Index was outside the bounds of the array.";

    #endregion

    #region Fields

    private int m_calls;

    #endregion

    #region ILogger

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Interlocked.Increment(ref m_calls);
        throw new IndexOutOfRangeException(MESSAGE);
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        Interlocked.Increment(ref m_calls);
        throw new IndexOutOfRangeException(MESSAGE);
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        Interlocked.Increment(ref m_calls);
        throw new IndexOutOfRangeException(MESSAGE);
    }

    #endregion

    #region Properties

    /// <summary>How many calls reached the logger.</summary>
    public int Calls => Volatile.Read(ref m_calls);

    #endregion
}
